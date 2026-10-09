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
}