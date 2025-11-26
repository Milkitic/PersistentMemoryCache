using System;
using System.IO;
using LiteDB;
using LiteDB.Engine;

namespace PersistentMemoryCache.Internal;

internal class PersistentLiteDatabase : LiteDatabase
{
    static PersistentLiteDatabase()
    {
        BsonMapper.Global.RegisterType<TimeSpan>
        (
            serialize: ts => new BsonValue(ts.TotalMilliseconds),
            deserialize: bson => TimeSpan.FromMilliseconds(bson.AsInt32)
        );
        BsonMapper.Global.RegisterType<DateTimeOffset>
        (
            serialize: dto => new BsonValue(dto.UtcDateTime),
            deserialize: bson => bson.AsDateTime.ToUniversalTime()
        );
    }

    /// <summary>
    /// Starts LiteDB database using a connection string for file system database
    /// </summary>
    internal PersistentLiteDatabase(string connectionString, BsonMapper mapper = null)
        : base(connectionString, mapper)
    {
    }

    /// <summary>
    /// Starts LiteDB database using a connection string for file system database
    /// </summary>
    internal PersistentLiteDatabase(ConnectionString connectionString, BsonMapper mapper = null)
        : base(connectionString, mapper)
    {
    }

    /// <summary>
    /// Starts LiteDB database using a generic Stream implementation (mostly MemoryStream).
    /// </summary>
    /// <param name="stream">DataStream reference </param>
    /// <param name="mapper">BsonMapper mapper reference</param>
    /// <param name="logStream">LogStream reference </param>
    internal PersistentLiteDatabase(Stream stream, BsonMapper mapper = null, Stream logStream = null)
        : base(stream, mapper, logStream)
    {
    }

    /// <summary>
    /// Start LiteDB database using a pre-exiting engine. When LiteDatabase instance dispose engine instance will be disposed too
    /// </summary>
    internal PersistentLiteDatabase(ILiteEngine engine, BsonMapper mapper = null, bool disposeOnClose = true)
        : base(engine, mapper, disposeOnClose)
    {
    }
}