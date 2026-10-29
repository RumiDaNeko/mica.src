namespace Mica {
 // The public half of the key Mica's releases are signed with (publish-release.ps1). An update is only
 // installed when its signature checks out against this, so a release swapped on GitHub, or a feed
 // pointing elsewhere, can't run on anyone's PC. The private half never leaves the publisher's machine.
 static class ReleaseKey {
  internal const string Public="<RSAKeyValue><Modulus>n5+CAGF7H7BBFha1zLzdbOCjYvY+4PxXzIQxUASfIV3pCUrMx6tLXTYa8hhCQIvGT6VWRFOWxKUzJBr1wFA0gVXTNNKbiA0cHfbkZDCl/AXTSE9qhMIhPpFp5xzKCwaOFBDvgBKE9kybeUW6ZoneI5Ajr3f3W1YKyLDxFvQY407pd7TA0Iu9FS2fhSF8sGIx0ltYTpEEFo4e/ZHiPL0RJ5Z2Ag45y/bUBtS/RkHFAR3SACvNbjDQouxNoOipyx6Rq/4NWidayZ/W5IcLm68TFJv0SNDFGSTWdKjqECGIiArhBUidgsaYLSc0e17e5xZRssMmaW9uaj03rMU/WTqEDf2yzJKMPIQDBgJyeXbBAEJ2XS+z3dbB638oBXM7HhJluOwH9E9vQI5jPvScz6R7E9G5EQEgJdR1dNe2S7aF7UoEumZt5DQunlD5rE57a7lfRc1RMdu/Z4QNyUGrU3g6H2Ri7zD8REl/C3Pz5hq33G0AwfmCq6tj0sfJHBPwAz/5</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
 }
}
