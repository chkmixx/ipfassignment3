using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum StorageClass
{
    Cold,
    Frozen
}

public sealed record Reading(
    string SensorId,
    DateTimeOffset Timestamp,
    StorageClass StorageClass,
    decimal Temperature);

public sealed record ImportError(
    int LineNumber,
    string RawLine,
    string Message);

public sealed record Alert(
    Reading Reading,
    bool OutsideRange,
    bool AbruptChange);

public sealed record ImportResult(
    IReadOnlyList<Reading> Readings,
    IReadOnlyList<ImportError> Errors);

public sealed record MonitoringArchive(
    IReadOnlyList<Reading> Readings,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<Alert> Alerts,
    DateTimeOffset CreatedAtUtc);

public static class ColdChainService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ImportResult ReadReadings(Stream input)
    {
        List<Reading> readings = new();
        List<ImportError> errors = new();

        Dictionary<string, StorageClass> sensorClasses =
            new(StringComparer.OrdinalIgnoreCase);

        using StreamReader reader = new(input, leaveOpen: true);

        int lineNumber = 0;

        while (reader.ReadLine() is string line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryParseReading(line, out Reading? reading, out string error))
            {
                errors.Add(new ImportError(lineNumber, line, error));
                continue;
            }

            if (sensorClasses.TryGetValue(
                    reading.SensorId,
                    out StorageClass previousClass)
                && previousClass != reading.StorageClass)
            {
                errors.Add(new ImportError(
                    lineNumber,
                    line,
                    "Storage class mismatch"));

                continue;
            }

            sensorClasses[reading.SensorId] = reading.StorageClass;
            readings.Add(reading);
        }

        return new ImportResult(
            Array.AsReadOnly(readings.ToArray()),
            Array.AsReadOnly(errors.ToArray()));
    }

    public static bool TryParseReading(
        string line,
        out Reading? reading,
        out string error)
    {
        reading = null;
        error = "";

        string[] parts = line.Split('|');

        if (parts.Length != 4)
        {
            error = "Expected 4 fields";
            return false;
        }

        string sensorId = parts[0].Trim();
        string timestampText = parts[1].Trim();
        string storageText = parts[2].Trim();
        string temperatureText = parts[3].Trim();

        if (sensorId.Length == 0)
        {
            error = "SensorId is empty";
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                timestampText,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset timestamp))
        {
            error = "Invalid timestamp";
            return false;
        }

        if (!Enum.TryParse<StorageClass>(
                storageText,
                true,
                out StorageClass storageClass)
            || !Enum.IsDefined(storageClass))
        {
            error = "Invalid storage class";
            return false;
        }

        if (!decimal.TryParse(
                temperatureText,
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal temperature))
        {
            error = "Invalid temperature";
            return false;
        }

        reading = new Reading(
            sensorId,
            timestamp,
            storageClass,
            temperature);

        return true;
    }

    public static IReadOnlyList<Alert> AnalyzeReadings(
        IReadOnlyList<Reading> readings)
    {
        List<Alert> alerts = new();

        var groups = readings.GroupBy(
            r => r.SensorId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            List<Reading> ordered = group
                .OrderBy(r => r.Timestamp)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                Reading current = ordered[i];

                bool outsideRange = IsOutsideRange(current);
                bool abruptChange = false;

                if (i > 0)
                {
                    Reading previous = ordered[i - 1];

                    abruptChange =
                        Math.Abs(
                            current.Temperature -
                            previous.Temperature) > 4.0m;
                }

                if (outsideRange || abruptChange)
                {
                    alerts.Add(new Alert(
                        current,
                        outsideRange,
                        abruptChange));
                }
            }
        }

        return alerts
            .OrderBy(
                a => a.Reading.SensorId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Reading.Timestamp)
            .ToList()
            .AsReadOnly();
    }

    public static bool IsOutsideRange(Reading reading)
    {
        if (reading.StorageClass == StorageClass.Cold)
        {
            return reading.Temperature < 2.0m ||
                   reading.Temperature > 8.0m;
        }

        return reading.Temperature < -22.0m ||
               reading.Temperature > -15.0m;
    }

    public static void WriteArchive(
        Stream output,
        MonitoringArchive archive)
    {
        JsonSerializer.Serialize(
            output,
            archive,
            JsonOptions);
    }

    public static MonitoringArchive ReadArchive(Stream input)
    {
        MonitoringArchive? archive =
            JsonSerializer.Deserialize<MonitoringArchive>(
                input,
                JsonOptions);

        if (archive is null)
        {
            throw new InvalidDataException(
                "Archive cannot be null.");
        }

        return archive;
    }
}

class Program
{
    static void Main()
    {
        using FileStream stream = new(
            "readings.txt", FileMode.Open, FileAccess.Read);

        ImportResult result = ColdChainService.ReadReadings(stream);

        Console.WriteLine($"Valid readings: {result.Readings.Count}");
        Console.WriteLine($"Import errors: {result.Errors.Count}");

        foreach (ImportError error in result.Errors)
        {
            Console.WriteLine($"Line {error.LineNumber}: {error.Message}");
        }

        IReadOnlyList<Alert> alerts =
            ColdChainService.AnalyzeReadings(result.Readings);

        Console.WriteLine($"Alerts: {alerts.Count}");

        foreach (Alert alert in alerts)
        {
            string reasons = "";

            if (alert.OutsideRange)
                reasons = "Outside range";

            if (alert.AbruptChange)
            {
                if (reasons.Length > 0)
                    reasons += "; ";

                reasons += "Abrupt change";
            }

            Console.WriteLine(
                $"{alert.Reading.SensorId} " +
                $"{alert.Reading.Timestamp:HH:mm} " +
                $"{alert.Reading.Temperature} -> {reasons}");
        }

        MonitoringArchive archive = new(
            result.Readings,
            result.Errors,
            alerts,
            DateTimeOffset.UtcNow);

        using (FileStream output = new(
                   "archive.json", FileMode.Create, FileAccess.Write))
        {
            ColdChainService.WriteArchive(output, archive);
        }

        using (MemoryStream memoryStream = new())
        {
            ColdChainService.WriteArchive(memoryStream, archive);
            memoryStream.Position = 0;

            MonitoringArchive restored =
                ColdChainService.ReadArchive(memoryStream);

            Console.WriteLine(
                $"Restored readings: {restored.Readings.Count}");
            Console.WriteLine(
                $"Restored errors: {restored.Errors.Count}");
            Console.WriteLine(
                $"Restored alerts: {restored.Alerts.Count}");
        }

        using FileStream input = new(
            "archive.json", FileMode.Open, FileAccess.Read);

        MonitoringArchive fileArchive =
            ColdChainService.ReadArchive(input);

        if (archive.Readings.Count != fileArchive.Readings.Count ||
            archive.Errors.Count != fileArchive.Errors.Count ||
            archive.Alerts.Count != fileArchive.Alerts.Count)
        {
            throw new InvalidDataException(
                "Archive round-trip verification failed.");
        }

        Console.WriteLine("Archive verification: OK");
    }
}
