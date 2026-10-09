using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ColdChainMonitor.Tests
{
    [TestClass]
    public class Test1
    {
        [TestMethod]
        public void ValidReading()
        {
            bool result = ColdChainService.TryParseReading(
                "S1|2026-10-09T08:00:00Z|Cold|5.0",
                out Reading? reading,
                out string error);

            Assert.IsTrue(result);
            Assert.IsNotNull(reading);
            Assert.AreEqual("", error);
        }

        [TestMethod]
        public void InvalidTimestamp()
        {
            bool result = ColdChainService.TryParseReading(
                "S1|wrong-date|Cold|5.0",
                out _,
                out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Invalid timestamp", error);
        }

        [TestMethod]
        public void InvalidStorageClass()
        {
            bool result = ColdChainService.TryParseReading(
                "S1|2026-10-09T08:00:00Z|Wrong|5.0",
                out _,
                out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Invalid storage class", error);
        }

        [TestMethod]
        public void InvalidTemperature()
        {
            bool result = ColdChainService.TryParseReading(
                "S1|2026-10-09T08:00:00Z|Cold|abc",
                out _,
                out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Invalid temperature", error);
        }

        [TestMethod]
        public void WrongFieldCount()
        {
            bool result = ColdChainService.TryParseReading(
                "S1|2026-10-09T08:00:00Z|Cold",
                out _,
                out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("Expected 4 fields", error);
        }

        [TestMethod]
        public void EmptySensorId()
        {
            bool result = ColdChainService.TryParseReading(
                "|2026-10-09T08:00:00Z|Cold|5.0",
                out _,
                out string error);

            Assert.IsFalse(result);
            Assert.AreEqual("SensorId is empty", error);
        }

        [TestMethod]
        public void StorageClassMismatch()
        {
            string data =
                "S1|2026-10-09T08:00:00Z|Cold|5.0\n" +
                "S1|2026-10-09T08:01:00Z|Frozen|-18.0";

            using MemoryStream stream =
                new(Encoding.UTF8.GetBytes(data));

            ImportResult result =
                ColdChainService.ReadReadings(stream);

            Assert.AreEqual(1, result.Readings.Count);
            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual(
                "Storage class mismatch",
                result.Errors[0].Message);
        }

        [TestMethod]
        public void ColdLowerBoundaryIsValid()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Cold,
                2.0m);

            Assert.IsFalse(
                ColdChainService.IsOutsideRange(reading));
        }

        [TestMethod]
        public void ColdUpperBoundaryIsValid()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Cold,
                8.0m);

            Assert.IsFalse(
                ColdChainService.IsOutsideRange(reading));
        }

        [TestMethod]
        public void FrozenLowerBoundaryIsValid()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Frozen,
                -22.0m);

            Assert.IsFalse(
                ColdChainService.IsOutsideRange(reading));
        }

        [TestMethod]
        public void FrozenUpperBoundaryIsValid()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Frozen,
                -15.0m);

            Assert.IsFalse(
                ColdChainService.IsOutsideRange(reading));
        }

        [TestMethod]
        public void ColdOutsideRangeCreatesAlert()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Cold,
                10.0m);

            var alerts =
                ColdChainService.AnalyzeReadings(
                    new[] { reading });

            Assert.AreEqual(1, alerts.Count);
            Assert.IsTrue(alerts[0].OutsideRange);
        }

        [TestMethod]
        public void FrozenOutsideRangeCreatesAlert()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Frozen,
                -25.0m);

            var alerts =
                ColdChainService.AnalyzeReadings(
                    new[] { reading });

            Assert.AreEqual(1, alerts.Count);
            Assert.IsTrue(alerts[0].OutsideRange);
        }

        [TestMethod]
        public void AbruptChangeCreatesAlert()
        {
            DateTimeOffset time =
                DateTimeOffset.UtcNow;

            Reading first = new(
                "S1",
                time,
                StorageClass.Cold,
                3.0m);

            Reading second = new(
                "S1",
                time.AddMinutes(1),
                StorageClass.Cold,
                8.0m);

            var alerts =
                ColdChainService.AnalyzeReadings(
                    new[] { first, second });

            Assert.AreEqual(1, alerts.Count);
            Assert.IsTrue(alerts[0].AbruptChange);
        }

        [TestMethod]
        public void ChangeOfExactlyFourIsNotAbrupt()
        {
            DateTimeOffset time =
                DateTimeOffset.UtcNow;

            Reading first = new(
                "S1",
                time,
                StorageClass.Cold,
                3.0m);

            Reading second = new(
                "S1",
                time.AddMinutes(1),
                StorageClass.Cold,
                7.0m);

            var alerts =
                ColdChainService.AnalyzeReadings(
                    new[] { first, second });

            Assert.AreEqual(0, alerts.Count);
        }

        [TestMethod]
        public void JsonRoundTripWorks()
        {
            Reading reading = new(
                "S1",
                DateTimeOffset.UtcNow,
                StorageClass.Cold,
                5.0m);

            ImportResult result = new(
                new[] { reading },
                Array.Empty<ImportError>());

            MonitoringArchive archive = new(
                result.Readings,
                result.Errors,
                Array.Empty<Alert>(),
                DateTimeOffset.UtcNow);

            using MemoryStream stream = new();

            ColdChainService.WriteArchive(
                stream,
                archive);

            stream.Position = 0;

            MonitoringArchive restored =
                ColdChainService.ReadArchive(stream);

            Assert.AreEqual(
                archive.Readings.Count,
                restored.Readings.Count);

            Assert.AreEqual(
                archive.Errors.Count,
                restored.Errors.Count);

            Assert.AreEqual(
                archive.Alerts.Count,
                restored.Alerts.Count);
        }

        [TestMethod]
        public void MultipleReadingsAreImported()
        {
            string data =
                "S1|2026-10-09T08:00:00Z|Cold|5.0\n" +
                "S2|2026-10-09T08:01:00Z|Frozen|-18.0\n" +
                "S3|2026-10-09T08:02:00Z|Cold|4.0";

            using MemoryStream stream =
                new(Encoding.UTF8.GetBytes(data));

            ImportResult result =
                ColdChainService.ReadReadings(stream);

            Assert.AreEqual(3, result.Readings.Count);
            Assert.AreEqual(0, result.Errors.Count);
        }
    }
}