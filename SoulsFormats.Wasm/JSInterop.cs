using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using SoulsFormats;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public partial class JSInterop
{
    [JSExport]
    public static string ReadSoulsFile(string formatName, byte[] data)
    {
        try
        {
            Type type = Type.GetType($"SoulsFormats.{formatName}, SoulsFormats") 
                     ?? Type.GetType($"SoulsFormats.{formatName}");
            if (type == null)
            {
                return JsonSerializer.Serialize(new { error = $"Format '{formatName}' not found." });
            }

            MethodInfo readMethod = type.GetMethod("Read", 
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                new[] { typeof(byte[]) },
                null);
            if (readMethod == null)
            {
                return JsonSerializer.Serialize(new { error = $"Format '{formatName}' does not have a Read(byte[]) method." });
            }

            object result = readMethod.Invoke(null, new object[] { data });
            
            var options = new JsonSerializerOptions { 
                IncludeFields = true, 
                WriteIndented = false,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles 
            };
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    [JSExport]
    public static byte[] WriteSoulsFile(string formatName, string json)
    {
        try
        {
            Type type = Type.GetType($"SoulsFormats.{formatName}, SoulsFormats") 
                     ?? Type.GetType($"SoulsFormats.{formatName}");
            if (type == null)
            {
                throw new Exception($"Format '{formatName}' not found.");
            }

            var options = new JsonSerializerOptions { 
                IncludeFields = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles 
            };
            options.Converters.Add(new CompressionInfoConverter());
            object instance = JsonSerializer.Deserialize(json, type, options);
            if (instance == null)
            {
                throw new Exception($"Failed to deserialize '{formatName}'.");
            }

            MethodInfo writeMethod = type.GetMethod("Write", Type.EmptyTypes);
            if (writeMethod == null)
            {
                throw new Exception($"Format '{formatName}' does not have a Write() method.");
            }

            return (byte[])writeMethod.Invoke(instance, null);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error writing {formatName}: {ex.Message}", ex);
        }
    }

    public class DcxData
    {
        public SoulsFormats.DCX.CompressionInfo CompressionInfo;
        public byte[] Bytes;
    }

    [JSExport]
    public static string DecompressDCX(byte[] data)
    {
        try
        {
            byte[] uncompressed = SoulsFormats.DCX.Decompress(data, out SoulsFormats.DCX.CompressionInfo compInfo);
            var result = new DcxData { CompressionInfo = compInfo, Bytes = uncompressed };
            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = false };
            options.Converters.Add(new CompressionInfoConverter());
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [JSExport]
    public static byte[] CompressDCX(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            options.Converters.Add(new CompressionInfoConverter());
            var parsed = JsonSerializer.Deserialize<DcxData>(json, options);
            if (parsed == null || parsed.Bytes == null || parsed.CompressionInfo == null)
                throw new Exception("Invalid DCX JSON data.");
            return SoulsFormats.DCX.Compress(parsed.Bytes, parsed.CompressionInfo);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error compressing DCX: {ex.Message}", ex);
        }
    }

    public class CompressionInfoConverter : System.Text.Json.Serialization.JsonConverter<SoulsFormats.DCX.CompressionInfo>
    {
        public override SoulsFormats.DCX.CompressionInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("Type", out var typeProp))
                    return new SoulsFormats.DCX.UnkCompressionInfo();
                
                int typeVal = typeProp.GetInt32();
                SoulsFormats.DCX.Type type = (SoulsFormats.DCX.Type)typeVal;

                switch (type)
                {
                    case SoulsFormats.DCX.Type.Unknown: return JsonSerializer.Deserialize<SoulsFormats.DCX.UnkCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.None: return JsonSerializer.Deserialize<SoulsFormats.DCX.NoCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCP_DFLT: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcpDfltCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCP_EDGE: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcpEdgeCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.Zlib: return JsonSerializer.Deserialize<SoulsFormats.DCX.ZlibCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_EDGE: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxEdgeCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_DFLT: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxDfltCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_KRAK: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxKrakCompressionInfo>(root.GetRawText(), options);
                    case SoulsFormats.DCX.Type.DCX_ZSTD: return JsonSerializer.Deserialize<SoulsFormats.DCX.DcxZstdCompressionInfo>(root.GetRawText(), options);
                    default: return new SoulsFormats.DCX.UnkCompressionInfo();
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, SoulsFormats.DCX.CompressionInfo value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
