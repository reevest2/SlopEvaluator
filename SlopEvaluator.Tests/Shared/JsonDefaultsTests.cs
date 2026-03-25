using System.Text.Json;
using System.Text.Json.Serialization;
using SlopEvaluator.Shared.Json;

namespace SlopEvaluator.Tests.Shared;

public class JsonDefaultsTests
{
    [Fact]
    public void Create_ReturnsCamelCaseNamingPolicy()
    {
        var options = JsonDefaults.Create();

        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
    }

    [Fact]
    public void Create_ReturnsWriteIndented()
    {
        var options = JsonDefaults.Create();

        Assert.True(options.WriteIndented);
    }

    [Fact]
    public void Create_IgnoresNullValues()
    {
        var options = JsonDefaults.Create();

        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
    }

    [Fact]
    public void Create_IncludesStringEnumConverter()
    {
        var options = JsonDefaults.Create();

        Assert.Contains(options.Converters, c => c is JsonStringEnumConverter);
    }

    [Fact]
    public void Create_SerializesPropertyNamesAsCamelCase()
    {
        var options = JsonDefaults.Create();
        var obj = new { MyProperty = 42 };
        var json = JsonSerializer.Serialize(obj, options);

        Assert.Contains("myProperty", json);
        Assert.DoesNotContain("MyProperty", json);
    }

    [Fact]
    public void Create_OmitsNullProperties()
    {
        var options = JsonDefaults.Create();
        var obj = new TestObj { Name = "test", Value = null };
        var json = JsonSerializer.Serialize(obj, options);

        Assert.Contains("name", json);
        Assert.DoesNotContain("value", json);
    }

    [Fact]
    public void Create_ReturnsNewInstanceEachTime()
    {
        var options1 = JsonDefaults.Create();
        var options2 = JsonDefaults.Create();

        Assert.NotSame(options1, options2);
    }

    private class TestObj
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }
}
