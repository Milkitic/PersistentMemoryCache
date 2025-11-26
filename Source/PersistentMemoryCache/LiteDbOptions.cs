using System;

namespace PersistentMemoryCache;

public class LiteDbOptions(string fileName)
{
    public string FileName { get; } = fileName ?? throw new ArgumentNullException(nameof(fileName));
}