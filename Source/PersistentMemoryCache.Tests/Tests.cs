using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace PersistentMemoryCache.Tests;

public class Tests
{
    private static PersistentMemoryCache GetCache() =>
        new(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions("Test.db"))));

    [Fact]
    public void InsertAndRetrieveString()
    {
        IMemoryCache cache = GetCache();
        string key = "TestKey";
        string value = "TestValue";
        cache.Set(key, value);
        cache.Dispose();
        cache = null;
        cache = GetCache();

        string result = cache.Get<string>(key);
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo("TestValue");
    }


    [Fact]
    public void InsertAndRetrieveListOfStrings()
    {
        IMemoryCache cache = GetCache();
        string key = "TestListKey";
        List<string> value = ["Value1", "Value2"];
        cache.Set(key, value);
        cache.Dispose();
        cache = null;
        cache = GetCache();

        var result = cache.Get<List<string>>(key);
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void InsertAndRetrieveEmptyList()
    {
        IMemoryCache cache = GetCache();
        string key = "TestEmptyListKey";
        List<string> value = [];
        cache.Set(key, value);
        cache.Dispose();
        cache = null;
        cache = GetCache();

        var result = cache.Get<List<string>>(key);
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void InsertAndRetrieveCustomType()
    {
        IMemoryCache cache = GetCache();
        string key = "TestCustomTypeKey";
        Customer value = new Customer { CustomerId = 1, Name = "Foo" };
        cache.Set(key, value);
        cache.Dispose();
        cache = null;
        cache = GetCache();

        var result = cache.Get(key);
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(value);
    }

    public class Customer
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
    }
}