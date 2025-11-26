using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace PersistentMemoryCache.Tests;

public class Tests
{
    private static PersistentMemoryCache GetCache(string dbName) =>
        new(new PersistentMemoryCacheOptions("Test", new LiteDbStore(new LiteDbOptions(dbName))));

    [Fact]
    public void InsertAndRetrieveString()
    {
        var dbName = $"{Guid.NewGuid()}.db";
        try
        {
            using (var cache1 = GetCache(dbName))
            {
                string key = "TestKey";
                string value = "TestValue";
                cache1.Set(key, value);
            }

            using (var cache2 = GetCache(dbName))
            {
                string key = "TestKey";
                string result = cache2.Get<string>(key);
                result.Should().NotBeNull();
                result.Should().BeEquivalentTo("TestValue");
            }
        }
        finally
        {
            try { File.Delete(dbName); } catch { }
        }
    }

    [Fact]
    public void InsertAndRetrieveListOfStrings()
    {
        var dbName = $"{Guid.NewGuid()}.db";
        List<string> value = ["Value1", "Value2"];
        try
        {
            using (var cache1 = GetCache(dbName))
            {
                string key = "TestListKey";
                cache1.Set(key, value);
            }

            using (var cache2 = GetCache(dbName))
            {
                string key = "TestListKey";
                var result = cache2.Get<List<string>>(key);
                result.Should().NotBeNull();
                result.Should().BeEquivalentTo(value);
            }
        }
        finally
        {
            try { File.Delete(dbName); } catch { }
        }
    }

    [Fact]
    public void InsertAndRetrieveEmptyList()
    {
        var dbName = $"{Guid.NewGuid()}.db";
        List<string> value = [];
        try
        {
            using (var cache1 = GetCache(dbName))
            {
                string key = "TestEmptyListKey";
                cache1.Set(key, value);
            }

            using (var cache2 = GetCache(dbName))
            {
                string key = "TestEmptyListKey";
                var result = cache2.Get<List<string>>(key);
                result.Should().NotBeNull();
                result.Should().BeEquivalentTo(value);
            }
        }
        finally
        {
            try { File.Delete(dbName); } catch { }
        }
    }

    [Fact]
    public void InsertAndRetrieveCustomType()
    {
        var dbName = $"{Guid.NewGuid()}.db";
        Customer value = new Customer { CustomerId = 1, Name = "Foo" };
        try
        {
            using (var cache1 = GetCache(dbName))
            {
                string key = "TestCustomTypeKey";
                cache1.Set(key, value);
            }

            using (var cache2 = GetCache(dbName))
            {
                string key = "TestCustomTypeKey";
                var result = cache2.Get(key);
                result.Should().NotBeNull();
                result.Should().BeEquivalentTo(value);
            }
        }
        finally
        {
            try { File.Delete(dbName); } catch { }
        }
    }

    public class Customer
    {
        public int CustomerId { get; set; }
        public string Name { get; set; }
    }
}