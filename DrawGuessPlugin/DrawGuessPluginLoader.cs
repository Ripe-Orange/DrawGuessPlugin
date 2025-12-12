using BepInEx;
using BepInEx.Logging;
using System.Collections.Generic;

namespace DrawGuessPlugin
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class DrawGuessPluginLoader : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private HarmonyLib.Harmony harmony;
        private List<IDrawGuessPluginModule> loadedModules = new List<IDrawGuessPluginModule>();
        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded.");

            // 初始化Harmony补丁
            harmony = new HarmonyLib.Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // 加载所有模块
            LoadModules();

            Log.LogInfo("DrawGuessLoader initialized successfully.");

        }
        private void LoadModules()
        {
            Log.LogInfo("开始加载DrawGuess插件模块...");

            // 加载PressureLine模块
            var pressureLineModule = new PressureLine();
            pressureLineModule.Initialize(this);
            loadedModules.Add(pressureLineModule);

            Log.LogInfo($"成功加载 {loadedModules.Count} 个模块");
        }

        private void OnDestroy()
        {
            // 卸载所有模块
            foreach (var module in loadedModules)
            {
                module.Uninitialize();
            }
            loadedModules.Clear();

            harmony?.UnpatchAll(MyPluginInfo.PLUGIN_GUID);
        }
    }

    // 模块接口，用于统一管理所有插件模块
    public interface IDrawGuessPluginModule
    {
        void Initialize(DrawGuessPluginLoader loader);
        void Uninitialize();
    }
}

