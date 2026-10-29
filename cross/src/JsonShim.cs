using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

// Core.cs saves the session with .NET Framework's JavaScriptSerializer, which .NET 8 doesn't have. This stands
// in for the part Core.cs uses, so the file is shared with the Windows build as it is, and writes the same
// JSON (dates as "\/Date(ms)\/"), so a session file reads the same on both.
namespace System.Web.Script.Serialization {
 public class JavaScriptSerializer {
  public int MaxJsonLength {get;set;}
  public int RecursionLimit {get;set;}
  static readonly JsonSerializerOptions options=Options();
  // Untyped JSON the way JavaScriptSerializer hands it out: objects as Dictionary<string,object>, arrays as object[],
  // whole numbers as int (or long), others as decimal.
  public object DeserializeObject(string input) {
   try {using(var doc=JsonDocument.Parse(input,new JsonDocumentOptions {MaxDepth=RecursionLimit>0?RecursionLimit:64}))return Plain(doc.RootElement);}
   catch(JsonException e) {throw new ArgumentException(e.Message,e);}
  }
  static object Plain(JsonElement e) {
   switch(e.ValueKind) {
    case JsonValueKind.Object: {var map=new System.Collections.Generic.Dictionary<string,object>();foreach(var p in e.EnumerateObject())map[p.Name]=Plain(p.Value);return map;}
    case JsonValueKind.Array: {var list=new System.Collections.Generic.List<object>();foreach(var v in e.EnumerateArray())list.Add(Plain(v));return list.ToArray();}
    case JsonValueKind.String: return e.GetString();
    case JsonValueKind.Number: {int i;long l;if(e.TryGetInt32(out i))return i;if(e.TryGetInt64(out l))return l;return e.GetDecimal();}
    case JsonValueKind.True: return true;
    case JsonValueKind.False: return false;
    default: return null;
   }
  }
  static JsonSerializerOptions Options() {
   var o=new JsonSerializerOptions {PropertyNameCaseInsensitive=true,IncludeFields=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};
   o.Converters.Add(new MsDateConverter());return o;
  }
  public string Serialize(object value) {return JsonSerializer.Serialize(value,value==null?typeof(object):value.GetType(),options);}
  public T Deserialize<T>(string input) {
   try {return JsonSerializer.Deserialize<T>(input,options);}
   catch(JsonException e) {throw new ArgumentException(e.Message,e);}
   catch(NotSupportedException e) {throw new InvalidOperationException(e.Message,e);}
  }
 }
 sealed class MsDateConverter : JsonConverter<DateTime> {
  static readonly DateTime epoch=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);
  public override DateTime Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options) {
   string text=reader.GetString();
   if(text!=null&&text.StartsWith("/Date(",StringComparison.Ordinal)&&text.EndsWith(")/",StringComparison.Ordinal)) {
    string ms=text.Substring(6,text.Length-8);int zone=ms.IndexOfAny(new[]{'+','-'},1);if(zone>0)ms=ms.Substring(0,zone);
    return epoch.AddMilliseconds(long.Parse(ms,CultureInfo.InvariantCulture));
   }
   return DateTime.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal);
  }
  public override void Write(Utf8JsonWriter writer,DateTime value,JsonSerializerOptions options) {
   long ms=(long)(value.ToUniversalTime()-epoch).TotalMilliseconds;
   writer.WriteRawValue("\"\\/Date("+ms.ToString(CultureInfo.InvariantCulture)+")\\/\"",true);
  }
 }
}
