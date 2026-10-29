using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xilium.CefGlue.Common.Shared.Helpers;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;
using Xilium.CefGlue.Common.Shared.Serialization;

namespace Xilium.CefGlue.BrowserProcess.ObjectBinding
{
    internal static partial class JavascriptHelper
    {
        private const string CefGlueGlobalScriptFileName = "CefGlueGlobalScript.js";
        private const string GlobalObjectName = "cefglue";
        private const string PromiseFactoryFunctionName = "createPromise";
        private const string InterceptorFactoryFunctionName = "createInterceptor";
        private const string EvaluateScriptFunctionName = "evaluateScript";
        private const string BindNativeFunctionName = "Bind";
        private const string UnbindNativeFunctionName = "Unbind";

        // Mica: CEF 154 removed V8 extensions (CefRegisterExtension), so the global script is no longer installed
        // once for every context by V8. InstallInContext runs it in each new context instead, as a function that
        // receives the native Bind/Unbind functions the extension used to declare with "native function".
        private static string _globalScriptFactory;
        private static CefV8Handler _builtinFunctionHandler;

        public static void Register(INativeObjectRegistry nativeObjectRegistry)
        {
            var currentType = typeof(JavascriptHelper);
            var currentNamespace = currentType.Namespace;
            var currentAssembly = currentType.Assembly;
            using (var stream = new StreamReader(currentAssembly.GetManifestResourceStream($"{currentNamespace}.{CefGlueGlobalScriptFileName}")))
            {
                var cefGlueGlobalScript = Regex.Replace(FillScriptPlaceholders(stream.ReadToEnd()), @"native\s+function\s+\w+\s*\(\s*\)\s*;", "");
                _globalScriptFactory = $"(function ({BindNativeFunctionName}, {UnbindNativeFunctionName}) {{\n{cefGlueGlobalScript}\nreturn {GlobalObjectName};\n}})";
                _builtinFunctionHandler = new V8BuiltinFunctionHandler(nativeObjectRegistry);
            }
        }

        /// <summary>
        /// Defines the global helper object in a newly created context, before any page script runs.
        /// </summary>
        public static void InstallInContext(CefV8Context context)
        {
            if (_globalScriptFactory == null || !context.Enter())
            {
                return;
            }
            try
            {
                if (!context.TryEval(_globalScriptFactory, "", 0, out var factory, out var exception) || factory == null || !factory.IsFunction)
                {
                    throw new InvalidOperationException("Couldn't install the " + GlobalObjectName + " helper: " + exception?.Message);
                }
                var bind = CefV8Value.CreateFunction(BindNativeFunctionName, _builtinFunctionHandler);
                var unbind = CefV8Value.CreateFunction(UnbindNativeFunctionName, _builtinFunctionHandler);
                var helper = factory.ExecuteFunctionWithContext(context, null, new[] { bind, unbind });
                if (helper != null && helper.IsObject)
                {
                    context.GetGlobal().SetValue(GlobalObjectName, helper, CefV8PropertyAttribute.DontEnum);
                }
            }
            finally
            {
                context.Exit();
            }
        }

        public static PromiseHolder CreatePromise(this CefV8Context context)
        {
            var promiseFactory = GetGlobalInnerValue(context, PromiseFactoryFunctionName);
            var promiseData = promiseFactory.ExecuteFunctionWithContext(context, null, new CefV8Value[0]); // create a promise and return the resolve and reject callbacks

            var promise = promiseData.GetValue("promise");
            var resolve = promiseData.GetValue("resolve");
            var reject = promiseData.GetValue("reject");

            CefObjectTracker.Untrack(promise);
            CefObjectTracker.Untrack(resolve);
            CefObjectTracker.Untrack(reject);

            return new PromiseHolder(promise, resolve, reject, context);
        }

        public static CefV8Value CreateInterceptorObject(this CefV8Context context, CefV8Value targetObj)
        {
            var interceptorFactory = GetGlobalInnerValue(context, InterceptorFactoryFunctionName);
            
            return interceptorFactory.ExecuteFunctionWithContext(context, null, new[] { targetObj });
        }

        private static CefV8Value GetGlobalInnerValue(CefV8Context context, string innerValueKey)
        {
            var global = context.GetGlobal();
            var cefGlueGlobal = global.GetValue(GlobalObjectName); // TODO what if cefGlueGlobal == null?
            
            return cefGlueGlobal.GetValue(innerValueKey);
        }

        public static string WrapScriptForEvaluation(string script)
        {
            return $"{GlobalObjectName}.{EvaluateScriptFunctionName}(function() {{{script}\n}})";
        }

        private static string FillScriptPlaceholders(string script)
        {
            var sb = new StringBuilder(script);
            return sb
                .Replace("$GlobalObjectName$", GlobalObjectName)
                .Replace("$JsonIdAttribute$", JsonAttributes.Id)
                .Replace("$JsonRefAttribute$", JsonAttributes.Ref)
                .Replace("$JsonValuesAttribute$", JsonAttributes.Values)
                .Replace("$DataMarkerLength$", DataMarkers.MarkerLength.ToString())
                .Replace("$StringMarker$", DataMarkers.StringMarker)
                .Replace("$DateTimeMarker$", DataMarkers.DateTimeMarker)
                .Replace("$BinaryMarker$", DataMarkers.BinaryMarker)
                .Replace("$PromiseFactoryFunctionName$", PromiseFactoryFunctionName)
                .Replace("$InterceptorFactoryFunctionName$", InterceptorFactoryFunctionName)
                .Replace("$BindNativeFunctionName$", BindNativeFunctionName)
                .Replace("$UnbindNativeFunctionName$", UnbindNativeFunctionName)
                .Replace("$EvaluateScriptFunctionName$", EvaluateScriptFunctionName)
                .ToString();
        }
    }
}
