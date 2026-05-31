using System.Globalization;
using Peerfluence.Converters;

namespace Peerfluence.HeadlessTests;

public class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public class NullToBoolConverterTests
    {
        private readonly NullToBoolConverter _sut = new();

        [Fact]
        public void Null_ReturnsFalse()
        {
            var result = _sut.Convert(null, typeof(bool), null, Culture);
            Assert.Equal(false, result);
        }

        [Fact]
        public void NonNull_ReturnsTrue()
        {
            var result = _sut.Convert(new object(), typeof(bool), null, Culture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void Null_Inverted_ReturnsTrue()
        {
            var sut = new NullToBoolConverter { Invert = true };
            var result = sut.Convert(null, typeof(bool), null, Culture);
            Assert.Equal(true, result);
        }

        [Fact]
        public void NonNull_Inverted_ReturnsFalse()
        {
            var sut = new NullToBoolConverter { Invert = true };
            var result = sut.Convert(new object(), typeof(bool), null, Culture);
            Assert.Equal(false, result);
        }
    }

    public class ByteSizeConverterTests
    {
        private readonly ByteSizeConverter _sut = new();

        [Fact]
        public void Null_ReturnsZeroB()
        {
            Assert.Equal("0 B", _sut.Convert(null, typeof(string), null, Culture));
        }

        [Fact]
        public void Zero_ReturnsZeroB()
        {
            Assert.Equal("0 B", _sut.Convert(0L, typeof(string), null, Culture));
        }

        [Fact]
        public void Bytes_FormatsCorrectly()
        {
            Assert.Equal("512 B", _sut.Convert(512L, typeof(string), null, Culture));
        }

        [Fact]
        public void Kilobytes_FormatsCorrectly()
        {
            var result = (string?)_sut.Convert(1024L, typeof(string), null, Culture);
            Assert.Equal("1 KB", result);
        }

        [Fact]
        public void Megabytes_FormatsCorrectly()
        {
            var result = (string?)_sut.Convert(1048576L, typeof(string), null, Culture);
            Assert.Equal("1 MB", result);
        }

        [Theory]
        [InlineData(42)]
        [InlineData(42L)]
        [InlineData(42.0)]
        [InlineData(42f)]
        [InlineData((short)42)]
        [InlineData((uint)42)]
        [InlineData((ushort)42)]
        [InlineData((byte)42)]
        public void AllNumericTypes_AreSupported(object value)
        {
            var result = _sut.Convert(value, typeof(string), null, Culture);
            Assert.IsType<string>(result);
            Assert.Contains("B", (string)result!);
        }
    }

    public class SpeedConverterTests
    {
        private readonly SpeedConverter _sut = new();

        [Fact]
        public void Zero_FormatsWithPerSecond()
        {
            var result = (string?)_sut.Convert(0L, typeof(string), null, Culture);
            Assert.Equal("0 B/s", result);
        }

        [Fact]
        public void Kilobytes_FormatsWithPerSecond()
        {
            var result = (string?)_sut.Convert(1024L, typeof(string), null, Culture);
            Assert.Equal("1 KB/s", result);
        }
    }
}
