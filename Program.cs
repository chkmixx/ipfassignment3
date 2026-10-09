using System.Globalization;

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

class Program
{
    static void Main()
    {
        using FileStream stream = new FileStream(
            "readings.txt",
            FileMode.Open,
            FileAccess.Read);

        ImportResult result = ReadReadings(stream);

        Console.WriteLine($"Valid readings: {result.Readings.Count}");
        Console.WriteLine($"Import errors: {result.Errors.Count}");

        foreach (ImportError error in result.Errors)
        {
            Console.WriteLine(
                $"Line {error.LineNumber}: {error.Message}");
        }
        IReadOnlyList<Alert> alerts =
    AnalyzeReadings(result.Readings);

        Console.WriteLine($"Alerts: {alerts.Count}");

        foreach (Alert alert in alerts)
        {
            string reasons = "";

            if (alert.OutsideRange)
            {
                reasons += "Outside range";
            }

            if (alert.AbruptChange)
            {
                if (reasons.Length > 0)
                {
                    reasons += "; ";
                }

                reasons += "Abrupt change";
            }

            Console.WriteLine(
                $"{alert.Reading.SensorId} " +
                $"{alert.Reading.Timestamp:HH:mm} " +
                $"{alert.Reading.Temperature} " +
                $"-> {reasons}");
        }
    }

    static ImportResult ReadReadings(Stream input)
    {
        List<Reading> readings = new();
        List<ImportError> errors = new();
        Dictionary<string, StorageClass> sensorClasses =
            new(StringComparer.OrdinalIgnoreCase);

        using StreamReader reader = new(
            input,
            leaveOpen: true);

        int lineNumber = 0;

        while (reader.ReadLine() is string line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParseReading(
                    line,
                    out Reading? reading,
                    out string error))
            {
                errors.Add(
                    new ImportError(
                        lineNumber,
                        line,
                        error));

                continue;
            }

            if (sensorClasses.TryGetValue(
                    reading.StorageClass == StorageClass.Cold
                        ? reading.SensorId
                        : reading.SensorId,
                    out StorageClass previousClass)
                && previousClass != reading.StorageClass)
            {
                errors.Add(
                    new ImportError(
                        lineNumber,
                        line,
                        "Storage class mismatch"));

                continue;
            }

            sensorClasses[reading.SensorId] =
                reading.StorageClass;

            readings.Add(reading);
        }

        return new ImportResult(
            Array.AsReadOnly(readings.ToArray()),
            Array.AsReadOnly(errors.ToArray()));
    }

    static bool TryParseReading(
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
                DateTimeStyles.AssumeUniversal,
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
    static IReadOnlyList<Alert> AnalyzeReadings(
    IReadOnlyList<Reading> readings)
    {
        List<Alert> alerts = new();

        var groups = readings
            .GroupBy(
                r => r.SensorId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ordered = group
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
                        Math.Abs(current.Temperature - previous.Temperature) > 4.0m;
                }

                if (outsideRange || abruptChange)
                {
                    alerts.Add(
                        new Alert(
                            current,
                            outsideRange,
                            abruptChange));
                }
            }
        }

        return alerts
            .OrderBy(a => a.Reading.SensorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Reading.Timestamp)
            .ToList()
            .AsReadOnly();
    }

    static bool IsOutsideRange(Reading reading)
    {
        if (reading.StorageClass == StorageClass.Cold)
        {
            return reading.Temperature < 2.0m ||
                   reading.Temperature > 8.0m;
        }

        return reading.Temperature < -22.0m ||
               reading.Temperature > -15.0m;
    }
}