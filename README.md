# ColdChainMonitor
Tomiris Chekalin
Group:IT-2503

# 1. What does the program do?
The program reads temperature data from readings.txt, checks the data, finds unsafe temperatures and sudden temperature changes, and saves the result to archive.json.

After that, the program reads the JSON archive again and checks that the data was saved correctly.

# 2. Input file

The input file is readings.txt.Each line contains:
<img width="1233" height="350" alt="image" src="https://github.com/user-attachments/assets/202234b3-be75-4639-a601-2d7397914379" />

The program checks the following:

- the sensor ID is not empty
- the timestamp has the correct format
- the storage class is Cold or Frozen
- the temperature has the correct decimal format
- one sensor cannot change its storage class

For the provided file:

- 10 input lines
- 6 valid readings
- 4 import errors
- <img width="1515" height="668" alt="image" src="https://github.com/user-attachments/assets/934d72bf-7101-4b09-baaa-88046a12bced" />


The invalid lines contain a bad timestamp, an invalid storage class, a comma decimal value, and a storage class mismatch.

# 3. Streams

The program uses Stream and FileStream to work with files.The methods ReadReadings, WriteArchive and ReadArchive receive a Stream from the caller.The caller owns the stream, so these methods do not close it.For example, StreamReader is created with leaveOpen: true.The FileStream objects created in Program.cs are owned by the program and are closed with using.The methods also use the current stream position and do not reset it automatically.

# 4. Why I do not use a lazy sequence
ReadReadings finishes reading the input before returning the result.I did not return a lazy sequence connected to StreamReader because the reader can already be closed when the returned sequence is used.The readings and errors are stored in collections first.This means they can still be used after the original stream is closed.

# 5. Temperature checking

There are two storage classes.

Cold:
- minimum: 2.0
- maximum: 8.0

Frozen:
- minimum: -22.0
- maximum: -15.0

The limits are inclusive.For example, Cold 8.0 is safe and Frozen -15.0 is safe.A temperature change is considered abrupt when the difference from the previous reading of the same sensor is greater than 4.0.The first reading of a sensor cannot have an abrupt change because there is no previous reading.

# 6. Comparing readings

Readings are first grouped by SensorId.This is important because readings from different sensors should not be compared.
After grouping, readings of each sensor are sorted by timestamp.Then every reading is compared with the previous reading of the same sensor.
For the provided data, there are two alerts:

S1 08:05 8.7 -> Outside range; Abrupt change

S2 08:06 -12.0 -> Outside range; Abrupt change

The result is sorted by sensor ID and timestamp so that the output is always predictable.

# 7. Enum validation

The program uses the StorageClass enum:

Cold
Frozen

Enum.TryParse is used to read the value, but it is not enough by itself.For example, the value 99 can be parsed as an enum value even though it is not declared in StorageClass.Therefore, the program also uses Enum.IsDefined.This makes sure that only Cold and Frozen are accepted.

# 8. Pure methods and file operations

I separated data processing from file operations.

TryParseReading only processes one input line.

IsOutsideRange only checks the temperature.

AnalyzeReadings works with already loaded readings and creates alerts.

These methods do not read files or print information to the console.

Reading and writing files is done in ReadReadings, WriteArchive, ReadArchive and Program.Main.

# 9. JSON

The project uses System.Text.Json.The JSON settings use camelCase property names and store enum values as text.

For example:

"storageClass": "Cold"

The JSON file contains:

- readings
- import errors
- alerts
- CreatedAtUtc

The program also reads archive.json back after saving it.

# 10. MemoryStream

The project also tests the archive using MemoryStream.After writing data to MemoryStream, the position is at the endBefore reading the same data, the program uses:
memoryStream.Position = 0;This moves the position back to the beginning.The restored archive is then compared with the original archive.Values and collection contents are compared instead of object references because deserialization creates new objects.

# 11. CreatedAtUtc and ordering

CreatedAtUtc is created in Program.cs using DateTimeOffset.UtcNow.The processing methods do not use the current time themselves.Alerts are ordered by SensorId and then by Timestamp.This makes the output predictable and makes the tests easier to check.

# 12. Debugging cases

The program handles several incorrect inputs.

Invalid timestamp:

S3|bad-date|Cold|3.5

Result:

Invalid timestamp

Invalid storage class:

S2|2026-09-22T08:11:00Z|99|-17.0

Result:

Invalid storage class

Invalid temperature:

S1|2026-09-22T08:20:00Z|Cold|5,5

Result:

Invalid temperature

Storage class mismatch:

If the same sensor was already accepted as Cold and later appears as Frozen, the new reading becomes an import error.

# 13. Program result

The program produces:

Valid readings: 6
Import errors: 4
Alerts: 2

Then the archive is restored:

Restored readings: 6
Restored errors: 4
Restored alerts: 2

Finally:

Archive verification: OK
<img width="1515" height="668" alt="image" src="https://github.com/user-attachments/assets/8d2a587b-a517-4780-a0dd-ee23ce43c9c5" />


# 14. Automated tests

The project contains a separate MSTest project.
Test result:
<img width="1611" height="920" alt="image" src="https://github.com/user-attachments/assets/3d9604c7-59a4-4126-baf0-eaa2f095cd2e" />


# 15. Conclusion

The project processes the provided temperature data, detects incorrect readings and temperature alerts, saves the result to JSON and successfully reads the archive back.

All 17 automated tests pass successfully.
