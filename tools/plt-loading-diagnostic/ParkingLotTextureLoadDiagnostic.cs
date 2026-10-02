using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    // Temporary diagnostic only: preserves the original renderer and texture
    // loading call. BEGIN without END identifies the interrupted native call.
    [HarmonyPatch]
    internal static class ParkingLotTextureLoadDiagnostic
    {
        private static readonly Type Renderer = typeof(Game.Rendering.AreaBatchSystem);
        private static readonly MethodInfo Target = Renderer.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "Load" && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType.IsByRef
                && m.GetParameters()[1].ParameterType == typeof(Material));
        private static readonly FieldInfo Gate = AccessTools.Field(Renderer, "m_TextureLoaded");
        private static FieldInfo _asset, _loaded;
        private static StreamWriter _writer;
        private static int _sequence;
        private static bool _failed;
        private const int MaximumRecords = 4096;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (Target == null || Gate == null) return false;
            var textureData = Target.GetParameters()[0].ParameterType.GetElementType();
            _asset = AccessTools.Field(textureData, "m_Asset");
            _loaded = AccessTools.Field(textureData, "m_IsLoaded");
            return _asset != null && _loaded != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => Target;

        [HarmonyPrefix]
        private static void Before(object __instance, object[] __args, out int __state)
        {
            __state = 0;
            if (_failed || _sequence >= MaximumRecords) return;
            try
            {
                // Match vanilla's gate; no log/allocation for already loaded slots.
                if ((bool)Gate.GetValue(__instance) || (bool)_loaded.GetValue(__args[0])) return;
                var asset = _asset.GetValue(__args[0]);
                if (asset == null) return;
                var material = __args[1] as Material;
                __state = ++_sequence;
                Write("BEGIN " + __state + " frame=" + Time.frameCount
                    + " material=" + (material == null ? "<null/destroyed>" : material.name)
                    + " property=" + __args[2]
                    + " asset=" + Describe(asset));
            }
            catch (Exception e) { Fail(e); }
        }

        [HarmonyPostfix]
        private static void After(int __state)
        {
            if (__state == 0 || _failed) return;
            try { Write("END " + __state); }
            catch (Exception e) { Fail(e); }
        }

        private static string Describe(object asset)
        {
            var type = asset.GetType();
            var result = type.Name;
            foreach (var name in new[] { "name", "guid", "path", "width", "height", "format", "isObjectLoaded" })
            {
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.GetIndexParameters().Length == 0)
                    result += " " + name + "=" + p.GetValue(asset);
            }
            return result.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void Write(string message)
        {
            if (_writer == null)
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Colossal Order", "Cities Skylines II", "Logs");
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "PLT-texture-load-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                Mod.log.Info("PLT texture diagnostic / 纹理诊断: " + path);
            }
            _writer.WriteLine(DateTime.Now.ToString("O") + " " + message);
            _writer.Flush();
            ((FileStream)_writer.BaseStream).Flush(true);
        }

        private static void Fail(Exception e)
        {
            _failed = true;
            Close();
            Mod.log.Warn("PLT texture diagnostic stopped / 纹理诊断停止: " + e.GetType().Name);
        }

        internal static void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
