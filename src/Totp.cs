using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Mica {
 // Authenticator codes (RFC 6238), like Google Authenticator: a site's 2FA setup key, saved with its login
 // in the encrypted vault, gives the 6-digit code for the current 30 seconds. Keys arrive as the
 // "otpauth://totp/…?secret=…" link a setup QR code holds, or as the plain key sites show to type in.
 static class Totp {
  internal sealed class Key {
   internal byte[] Secret;internal int Digits=6,Period=30;internal string Algorithm="SHA1",Issuer,Account;
  }
  const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  // Throws ArgumentException for anything that isn't a usable key.
  internal static Key Parse(string text) {
   if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter the site's 2FA setup key.");
   text=text.Trim();var key=new Key();
   if(text.StartsWith("otpauth://",StringComparison.OrdinalIgnoreCase)) {
    Uri uri;if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||!uri.Host.Equals("totp",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only time-based (TOTP) 2FA keys are supported.");
    string label=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));int colon=label.IndexOf(':');
    if(colon>=0){key.Issuer=label.Substring(0,colon).Trim();key.Account=label.Substring(colon+1).Trim();}else if(label.Length>0)key.Account=label.Trim();
    string secret=null;
    foreach(var pair in uri.Query.TrimStart('?').Split('&')) {
     int at=pair.IndexOf('=');if(at<=0)continue;string name=pair.Substring(0,at).ToLowerInvariant(),value=Uri.UnescapeDataString(pair.Substring(at+1).Replace('+',' '));
     int number;
     if(name=="secret")secret=value;
     else if(name=="issuer"&&value.Length>0)key.Issuer=value;
     else if(name=="digits"&&int.TryParse(value,out number))key.Digits=number;
     else if(name=="period"&&int.TryParse(value,out number))key.Period=number;
     else if(name=="algorithm")key.Algorithm=value.ToUpperInvariant();
    }
    key.Secret=Base32(secret);
   }else key.Secret=Base32(text);
   if(key.Digits<6||key.Digits>8)throw new ArgumentException("This 2FA key asks for "+key.Digits+"-digit codes, which Mica can't make.");
   if(key.Period<10||key.Period>300)throw new ArgumentException("This 2FA key has an unusual time step.");
   if(key.Algorithm!="SHA1"&&key.Algorithm!="SHA256"&&key.Algorithm!="SHA512")throw new ArgumentException("This 2FA key uses an algorithm Mica doesn't know.");
   return key;
  }
  internal static bool TryParse(string text,out Key key) {try {key=Parse(text);return true;}catch(ArgumentException){key=null;return false;}}
  // Spaces, dashes and padding are ignored; letters may be any case.
  internal static byte[] Base32(string text) {
   string clean=new string((text??"").Where(c=>!char.IsWhiteSpace(c)&&c!='-'&&c!='=').Select(char.ToUpperInvariant).ToArray());
   if(clean.Length<16||clean.Length>128)throw new ArgumentException("That doesn't look like a 2FA setup key.");
   var bytes=new byte[clean.Length*5/8];int buffer=0,bits=0,index=0;
   foreach(char c in clean) {
    int value=Alphabet.IndexOf(c);if(value<0)throw new ArgumentException("That doesn't look like a 2FA setup key.");
    buffer=(buffer<<5)|value;bits+=5;
    if(bits>=8){bytes[index++]=(byte)(buffer>>(bits-8));bits-=8;buffer&=(1<<bits)-1;}
   }
   return bytes;
  }
  static string ToBase32(byte[] bytes) {
   var text=new StringBuilder();int buffer=0,bits=0;
   foreach(byte b in bytes){buffer=(buffer<<8)|b;bits+=8;while(bits>=5){text.Append(Alphabet[(buffer>>(bits-5))&31]);bits-=5;}buffer&=(1<<bits)-1;}
   if(bits>0)text.Append(Alphabet[(buffer<<(5-bits))&31]);
   return text.ToString();
  }
  // How a key is kept in the vault: one form, whatever it was saved from.
  internal static string Normalize(Key key) {
   return "otpauth://totp/"+Uri.EscapeDataString(key.Account??"")+"?secret="+ToBase32(key.Secret)+"&digits="+key.Digits+"&period="+key.Period+"&algorithm="+key.Algorithm+(string.IsNullOrEmpty(key.Issuer)?"":"&issuer="+Uri.EscapeDataString(key.Issuer));
  }
  internal static string Code(Key key,DateTime utc) {
   long step=(long)Math.Floor((utc-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds/key.Period);
   var counter=BitConverter.GetBytes(step);if(BitConverter.IsLittleEndian)Array.Reverse(counter);
   byte[] hash;
   using(HMAC mac=key.Algorithm=="SHA256"?(HMAC)new HMACSHA256(key.Secret):key.Algorithm=="SHA512"?(HMAC)new HMACSHA512(key.Secret):new HMACSHA1(key.Secret))hash=mac.ComputeHash(counter);
   int offset=hash[hash.Length-1]&0x0F;
   int binary=((hash[offset]&0x7F)<<24)|(hash[offset+1]<<16)|(hash[offset+2]<<8)|hash[offset+3];
   return (binary%(int)Math.Pow(10,key.Digits)).ToString().PadLeft(key.Digits,'0');
  }
  internal static string Code(string saved) {Key key;return TryParse(saved,out key)?Code(key,DateTime.UtcNow):null;}
  internal static int SecondsLeft(string saved) {Key key;if(!TryParse(saved,out key))return 0;long now=(long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;return key.Period-(int)(now%key.Period);}
 }
}
