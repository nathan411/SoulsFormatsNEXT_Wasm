using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using SoulsFormats;

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

            MethodInfo readMethod = type.GetMethod("Read", new[] { typeof(byte[]) });
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
            return JsonSerializer.Serialize(new { error = ex.Message, stackTrace = ex.StackTrace });
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
            // Note: JSExport doesn't easily return union types (byte[] OR string error).
            // A common pattern is returning an empty array on error, and maybe logging it, 
            // or we could throw the exception so JS can catch it.
            throw new Exception($"Error writing {formatName}: {ex.Message}", ex);
        }
    }
}
