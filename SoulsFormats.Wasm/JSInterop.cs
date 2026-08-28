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

    public class ParamDefFieldDTO
    {
        public string DisplayName { get; set; }
        public string InternalName { get; set; }
        public string DisplayType { get; set; }
        public string InternalType { get; set; }
        public string Description { get; set; }
        public string DisplayFormat { get; set; }
        public object Default { get; set; }
        public object Minimum { get; set; }
        public object Maximum { get; set; }
        public object Increment { get; set; }
        public int SortID { get; set; }
        public int ArrayLength { get; set; }
        public int BitSize { get; set; }
    }

    public class ParamRowDTO
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public Dictionary<string, object> Cells { get; set; }
    }

    public class ParamFileDTO
    {
        public string ParamType { get; set; }
        public short ParamdefDataVersion { get; set; }
        public bool BigEndian { get; set; }
        public byte Format2D { get; set; }
        public byte Format2E { get; set; }
        public byte ParamdefFormatVersion { get; set; }
        public short Unk06 { get; set; }
        public bool UnnamedRows { get; set; }
        public bool HeaderlessRows { get; set; }
        public List<ParamDefFieldDTO> Fields { get; set; }
        public List<ParamRowDTO> Rows { get; set; }
    }

    [JSExport]
    public static string GetParamInfo(byte[] data)
    {
        try
        {
            var param = PARAM.Read(data);
            return JsonSerializer.Serialize(new {
                paramType = param.ParamType,
                paramdefDataVersion = param.ParamdefDataVersion,
                bigEndian = param.BigEndian,
                detectedSize = param.DetectedSize,
                rowCount = param.Rows != null ? param.Rows.Count : 0,
                unnamedRows = param.UnnamedRows,
                headerlessRows = param.HeaderlessRows
            });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg });
        }
    }

    [JSExport]
    public static string ReadParamWithDef(byte[] data, string paramdefXml)
    {
        try
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(paramdefXml));
            var paramdef = PARAMDEF.XmlDeserialize(ms);

            var param = PARAM.Read(data);
            param.ApplyParamdef(paramdef);

            var fields = new List<ParamDefFieldDTO>();
            foreach (var field in paramdef.Fields)
            {
                fields.Add(new ParamDefFieldDTO {
                    DisplayName = field.DisplayName,
                    InternalName = field.InternalName,
                    DisplayType = field.DisplayType.ToString(),
                    InternalType = field.InternalType,
                    Description = field.Description,
                    DisplayFormat = field.DisplayFormat,
                    Default = field.Default,
                    Minimum = field.Minimum,
                    Maximum = field.Maximum,
                    Increment = field.Increment,
                    SortID = field.SortID,
                    ArrayLength = field.ArrayLength,
                    BitSize = field.BitSize
                });
            }

            var rows = new List<ParamRowDTO>();
            foreach (var row in param.Rows)
            {
                var cellDict = new Dictionary<string, object>();
                if (row.Cells != null)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Def != null && !string.IsNullOrEmpty(cell.Def.InternalName))
                        {
                            cellDict[cell.Def.InternalName] = cell.Value;
                        }
                    }
                }
                rows.Add(new ParamRowDTO {
                    ID = row.ID,
                    Name = row.Name,
                    Cells = cellDict
                });
            }

            var dto = new ParamFileDTO {
                ParamType = param.ParamType,
                ParamdefDataVersion = param.ParamdefDataVersion,
                BigEndian = param.BigEndian,
                Format2D = (byte)param.Format2D,
                Format2E = (byte)param.Format2E,
                ParamdefFormatVersion = param.ParamdefFormatVersion,
                Unk06 = param.Unk06,
                UnnamedRows = param.UnnamedRows,
                HeaderlessRows = param.HeaderlessRows,
                Fields = fields,
                Rows = rows
            };

            return JsonSerializer.Serialize(dto);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }

    [JSExport]
    public static byte[] WriteParamWithDef(string paramJson, string paramdefXml)
    {
        try
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(paramdefXml));
            var paramdef = PARAMDEF.XmlDeserialize(ms);

            var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
            var dto = JsonSerializer.Deserialize<ParamFileDTO>(paramJson, options);
            if (dto == null)
                throw new Exception("Failed to deserialize ParamFileDTO JSON.");

            var param = new PARAM
            {
                ParamType = dto.ParamType ?? paramdef.ParamType,
                ParamdefDataVersion = dto.ParamdefDataVersion,
                BigEndian = dto.BigEndian,
                Format2D = (PARAM.FormatFlags1)dto.Format2D,
                Format2E = (PARAM.FormatFlags2)dto.Format2E,
                ParamdefFormatVersion = dto.ParamdefFormatVersion,
                Unk06 = dto.Unk06,
                UnnamedRows = dto.UnnamedRows,
                HeaderlessRows = dto.HeaderlessRows,
                Rows = new List<PARAM.Row>()
            };

            typeof(PARAM).GetProperty("AppliedParamdef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(param, paramdef);

            foreach (var r in dto.Rows)
            {
                var row = new PARAM.Row(r.ID, r.Name, paramdef);
                if (r.Cells != null)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.Def != null && r.Cells.TryGetValue(cell.Def.InternalName, out var rawVal))
                        {
                            SetCellValue(cell, rawVal);
                        }
                    }
                }
                param.Rows.Add(row);
            }

            return param.Write();
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception($"Error writing PARAM: {msg}", ex);
        }
    }

    private static void SetCellValue(PARAM.Cell cell, object rawVal)
    {
        if (rawVal == null) return;

        if (rawVal is JsonElement elem)
        {
            switch (cell.Def.DisplayType)
            {
                case PARAMDEF.DefType.s8:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetSByte() : sbyte.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u8:
                    if (cell.Def.ArrayLength > 1 && elem.ValueKind == JsonValueKind.String)
                        cell.Value = Convert.FromBase64String(elem.GetString() ?? "");
                    else
                        cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetByte() : byte.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.s16:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetInt16() : short.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u16:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetUInt16() : ushort.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.s32:
                case PARAMDEF.DefType.b32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetInt32() : int.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.u32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetUInt32() : uint.Parse(elem.GetString() ?? "0");
                    break;
                case PARAMDEF.DefType.f32:
                case PARAMDEF.DefType.angle32:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetSingle() : float.Parse(elem.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case PARAMDEF.DefType.f64:
                    cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetDouble() : double.Parse(elem.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case PARAMDEF.DefType.fixstr:
                case PARAMDEF.DefType.fixstrW:
                    cell.Value = elem.GetString() ?? "";
                    break;
                case PARAMDEF.DefType.dummy8:
                    if (cell.Def.BitSize == -1 && cell.Def.ArrayLength > 1 && elem.ValueKind == JsonValueKind.String)
                        cell.Value = Convert.FromBase64String(elem.GetString() ?? "");
                    else
                        cell.Value = elem.ValueKind == JsonValueKind.Number ? elem.GetByte() : (byte.TryParse(elem.GetString() ?? "0", out var b) ? b : (byte)0);
                    break;
                default:
                    cell.Value = elem.ToString();
                    break;
            }
        }
        else
        {
            cell.Value = rawVal;
        }
    }

    public class FlverMeshDTO
    {
        public string MaterialName { get; set; }
        public float[] Positions { get; set; }
        public float[] Normals { get; set; }
        public int[] Indices { get; set; }
    }

    public class FlverGeometryDTO
    {
        public string HeaderVersion { get; set; }
        public List<FlverMeshDTO> Meshes { get; set; }
        public List<string> Nodes { get; set; }
    }

    [JSExport]
    public static string ReadFlverGeometry(byte[] data)
    {
        try
        {
            List<FlverMeshDTO> meshList = new List<FlverMeshDTO>();
            List<string> nodeNames = new List<string>();
            string versionStr = "";

            if (FLVER2.Is(data))
            {
                var flver = FLVER2.Read(data);
                versionStr = flver.Header.Version.ToString("X");

                if (flver.Nodes != null)
                {
                    foreach (var node in flver.Nodes)
                    {
                        nodeNames.Add(node.Name ?? "");
                    }
                }

                for (int mIdx = 0; mIdx < flver.Meshes.Count; mIdx++)
                {
                    var mesh = flver.Meshes[mIdx];
                    var matName = (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                        ? flver.Materials[mesh.MaterialIndex].Name
                        : $"Mesh_{mIdx}";

                    List<float> positions = new List<float>(mesh.Vertices.Count * 3);
                    List<float> normals = new List<float>(mesh.Vertices.Count * 3);

                    foreach (var v in mesh.Vertices)
                    {
                        positions.Add(v.Position.X);
                        positions.Add(v.Position.Y);
                        positions.Add(v.Position.Z);

                        normals.Add(v.Normal.X);
                        normals.Add(v.Normal.Y);
                        normals.Add(v.Normal.Z);
                    }

                    List<int> indices = new List<int>();
                    if (mesh.FaceSets != null && mesh.FaceSets.Count > 0)
                    {
                        var fs = mesh.FaceSets.FirstOrDefault(f => (f.Flags & FLVER2.FaceSet.FSFlags.LodLevel1) == 0 && (f.Flags & FLVER2.FaceSet.FSFlags.LodLevel2) == 0 && (f.Flags & FLVER2.FaceSet.FSFlags.MotionBlur) == 0)
                              ?? mesh.FaceSets[0];

                        indices = fs.Triangulate(mesh.Vertices.Count < 0xFFFF);
                    }

                    meshList.Add(new FlverMeshDTO {
                        MaterialName = matName,
                        Positions = positions.ToArray(),
                        Normals = normals.ToArray(),
                        Indices = indices.ToArray()
                    });
                }
            }
            else if (FLVER0.Is(data))
            {
                var flver = FLVER0.Read(data);
                versionStr = "FLVER0";

                if (flver.Nodes != null)
                {
                    foreach (var node in flver.Nodes)
                    {
                        nodeNames.Add(node.Name ?? "");
                    }
                }

                for (int mIdx = 0; mIdx < flver.Meshes.Count; mIdx++)
                {
                    var mesh = flver.Meshes[mIdx];
                    var matName = (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < flver.Materials.Count)
                        ? flver.Materials[mesh.MaterialIndex].Name
                        : $"Mesh_{mIdx}";

                    List<float> positions = new List<float>(mesh.Vertices.Count * 3);
                    List<float> normals = new List<float>(mesh.Vertices.Count * 3);

                    foreach (var v in mesh.Vertices)
                    {
                        positions.Add(v.Position.X);
                        positions.Add(v.Position.Y);
                        positions.Add(v.Position.Z);

                        normals.Add(v.Normal.X);
                        normals.Add(v.Normal.Y);
                        normals.Add(v.Normal.Z);
                    }

                    List<int> indices = new List<int>();
                    if (mesh.Indices != null)
                    {
                        indices = new List<int>(mesh.Indices);
                    }

                    meshList.Add(new FlverMeshDTO {
                        MaterialName = matName,
                        Positions = positions.ToArray(),
                        Normals = normals.ToArray(),
                        Indices = indices.ToArray()
                    });
                }
            }
            else
            {
                return JsonSerializer.Serialize(new { error = "File is not a recognized FLVER0 or FLVER2 binary." });
            }

            var dto = new FlverGeometryDTO {
                HeaderVersion = versionStr,
                Meshes = meshList,
                Nodes = nodeNames
            };

            return JsonSerializer.Serialize(dto);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return JsonSerializer.Serialize(new { error = msg, stackTrace = ex.StackTrace });
        }
    }
}
