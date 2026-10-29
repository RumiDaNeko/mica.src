# Mica's own R8 settings, on top of proguard-android-optimize.txt.
# Mica uses no reflection, no @JavascriptInterface and no serialization by class name: pages talk to it
# through WebMessageListener strings and JSON it parses itself, so everything can be renamed and inlined.

# All classes in one package with short names, and private members opened up so R8 can inline across classes.
-repackageclasses ''
-allowaccessmodification

# Debug, verbose and info logging go from release builds entirely (warnings and errors stay).
-assumenosideeffects class android.util.Log {
    public static int d(...);
    public static int v(...);
    public static int i(...);
}

# Kotlin's null checks on parameters of Mica's own code are compile-time guarantees here.
-assumenosideeffects class kotlin.jvm.internal.Intrinsics {
    public static void checkNotNullParameter(java.lang.Object, java.lang.String);
    public static void checkParameterIsNotNull(java.lang.Object, java.lang.String);
}

# Crash reports stay readable with the mapping file that ships inside the bundle.
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile
