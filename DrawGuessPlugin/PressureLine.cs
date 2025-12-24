using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using Shapes;

namespace DrawGuessPlugin
{
    /// <summary>
    /// 压力感应线条模块，用于管理压力线条的创建和同步
    /// </summary>
    public class PressureLine : IDrawGuessPluginModule
    {
        internal static readonly Dictionary<DrawModule, PressureLineCreator> ActiveCreators = new();
        internal static readonly Dictionary<int, List<DrawableElement>> PressureGroups = new();

        /// <summary>
        /// 注册线段到压力线条组
        /// </summary>
        internal static void RegisterSegment(int groupId, DrawableElement elem)
        {
            if (!PressureGroups.TryGetValue(groupId, out var list))
            {
                list = new List<DrawableElement>();
                PressureGroups[groupId] = list;
            }
            list.Add(elem);
        }

        /// <summary>
        /// 清除指定ID的压力线条组
        /// </summary>
        internal static void ClearGroup(int groupId)
        {
            if (PressureGroups.ContainsKey(groupId)) PressureGroups.Remove(groupId);
        }
        
        /// <summary>
        /// 初始化压力感应线条模块
        /// </summary>
        public void Initialize(DrawGuessPluginLoader loader)
        {
        }

        /// <summary>
        /// 卸载压力感应线条模块
        /// </summary>
        public void Uninitialize()
        {
        }
    }

    /// <summary>
    /// 多线条撤销操作，用于撤销一组压力线条
    /// </summary>
    public class UndoMultiDrawElement : IUndo
    {
        private List<DrawableElement> lines;

        public UndoMultiDrawElement(IEnumerable<DrawableElement> drawnLines)
        {
            this.lines = new List<DrawableElement>(drawnLines);
        }

        public void Redo()
        {
            foreach (var line in lines)
            {
                if (line != null)
                {
                    line.MarkAsDeleted(false);
                    if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
                    {
                        LobbyManager.Instance.SyncController.LobbyAddDrawLine(line.ToLineInformation());
                    }
                }
            }
        }

        public void Undo()
        {
            foreach (var line in lines)
            {
                if (line != null)
                {
                    line.MarkAsDeleted(true);
                    if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null && LobbyDrawModule.Instance != null)
                    {
                        string ownerId = AccessTools.Property(typeof(DrawModule), "LocalPlayfabId").GetValue(LobbyDrawModule.Instance) as string;
                        if (string.IsNullOrEmpty(ownerId)) ownerId = "local";
                        LobbyManager.Instance.SyncController.LobbyDrawingDeletePrecise(ownerId, line.Ident);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 压感补丁类，包含所有与压感相关的补丁方法
    /// </summary>
    public static class PressurePatches
    {
        [HarmonyPatch(typeof(LinePressure))]
        [HarmonyPatch("Activate")]
        [HarmonyPrefix]
        public static bool LinePressureActivatePrefix(LinePressure __instance)
        {
            __instance.enabled = false;
            return false;
        }

        [HarmonyPatch(typeof(LinePressure))]
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool LinePressureUpdatePrefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(DrawModule), "ActivateTool")]
    public static class DrawModuleActivateToolPatch
    {
        [HarmonyPostfix]
        public static void ActivateToolPostfix(DrawModule __instance, DrawModule.DrawTool tool)
        {
            try
            {
                foreach (var group in PressureLine.PressureGroups.Values)
                {
                    foreach (var elem in group)
                    {
                        if (elem is DrawableShape shape)
                        {
                            if (tool == DrawModule.DrawTool.Eraser)
                                shape.LineErase.SetLineSelectActive(true);
                            else if (tool == DrawModule.DrawTool.Fill)
                            {
                                shape.LineFillable.SetLineSelectActive(true);
                                shape.SetHoverActive(true);
                            }
                            else if (tool == DrawModule.DrawTool.Dropper)
                                shape.LineDropperable.SetLineSelectActive(true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"激活工具后处理失败: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(DrawModule), "DeletedPrecise")]
    public static class DrawModuleDeletedPrecisePatch
    {
        [HarmonyPrefix]
        public static bool DeletedPrecisePrefix(DrawModule __instance, DrawableElement drawable)
        {
            int groupId = -1;
            foreach (var kvp in PressureLine.PressureGroups)
            {
                if (kvp.Value.Contains(drawable))
                {
                    groupId = kvp.Key;
                    break;
                }
            }

            if (groupId != -1)
            {
                var groupElements = new List<DrawableElement>(PressureLine.PressureGroups[groupId]);
                
                // 添加组合撤销事件
                __instance.UndoSystem.AddEvent(new UndoMultiEraseElement(groupElements));
                
                // 更新撤销按钮状态
                var drawingToolHub = AccessTools.Field(typeof(DrawModule), "DrawingToolHub").GetValue(__instance) as DrawingToolHub;
                if (drawingToolHub != null) drawingToolHub.SetUndoButtons();

                // 标记所有相关元素为删除状态
                foreach (var elem in groupElements)
                {
                    if (elem != null)
                    {
                        elem.MarkAsDeleted(true);
                        // 级联更新UI状态
                        Traverse.Create(__instance).Method("OnSendDeletePrecise", new object[] { elem.Ident }).GetValue();
                    }
                }
                
                // 返回false拦截原逻辑，避免只删除选中的那一段
                return false;
            }
            
            return true;
        }
    }

    /// <summary>
    /// 多线条擦除撤销操作，用于撤销一组线条的擦除
    /// </summary>
    public class UndoMultiEraseElement : IUndo
    {
        private List<DrawableElement> lines;

        public UndoMultiEraseElement(IEnumerable<DrawableElement> lines)
        {
            this.lines = new List<DrawableElement>(lines);
        }

        public void Undo()
        {
            // 撤销擦除 -> 恢复显示
            foreach (var line in lines)
            {
                if (line != null) line.MarkAsDeleted(false);
            }
        }

        public void Redo()
        {
            // 重做擦除 -> 删除隐藏
            foreach (var line in lines)
            {
                if (line != null)
                {
                    line.MarkAsDeleted(true);
                    if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null && LobbyDrawModule.Instance != null)
                    {
                        string ownerId = AccessTools.Property(typeof(DrawModule), "LocalPlayfabId").GetValue(LobbyDrawModule.Instance) as string;
                        if (string.IsNullOrEmpty(ownerId)) ownerId = "local";
                        LobbyManager.Instance.SyncController.LobbyDrawingDeletePrecise(ownerId, line.Ident);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(DrawModule), "OnUpdateTick")]
    public static class DrawModuleOnUpdateTickPatch
    {
        [HarmonyPrefix]
        public static bool OnUpdateTickPrefix(DrawModule __instance)
        {
            // 如果未启用压感或当前工具不是笔刷，则执行原逻辑
            if (!MenuGameSettings.UsePenPressure.Value) return true;
            if (__instance.ActiveDrawTool != DrawModule.DrawTool.Brush) return true;

            // 检查是否为Stage模式或远程同步模块，如果是则跳过压感逻辑
            if (IsStageOrSyncModule(__instance))
            {
                return true;
            }

            // 获取输入状态
            bool primaryDown = DrawInput.GetPrimaryDown();
            bool primary = DrawInput.GetPrimary();
            bool primaryUp = DrawInput.GetPrimaryUp();

            // 处理线条结束（鼠标/笔抬起）
            if (primaryUp)
            {
                if (PressureLine.ActiveCreators.TryGetValue(__instance, out var creator))
                {
                    creator.FinishLine();
                    PressureLine.ActiveCreators.Remove(__instance);
                    return false;
                }
            }

            // 处理线条开始（鼠标/笔按下）
            if (primaryDown)
            {
                Vector2 worldPoint;
                // 必须同时满足：在绘图区域内，且直接在绘图表面上（未被UI遮挡）
                if (__instance.GetWorldCoordsIfOverDrawSurface(out worldPoint) && DrawModule.IsDirectlyOverDrawSurface())
                {
                    StartPressureLine(__instance, worldPoint);
                    return false;
                }
            }

            // 处理线条绘制中（鼠标/笔按住）
            if (primary)
            {
                if (PressureLine.ActiveCreators.TryGetValue(__instance, out var creator))
                {
                    Vector2 worldPoint;
                    if (__instance.GetWorldCoordsIfOverDrawSurface(out worldPoint))
                    {
                        float pressure = (Pen.current != null) ? Mathf.Max(Pen.current.pressure.ReadValue(), 0.001f) : 1f;
                        creator.AppendPoint(worldPoint, pressure);
                    }
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 检查是否为Stage或同步模块，这些模块需要特殊处理
        /// </summary>
        private static bool IsStageOrSyncModule(DrawModule instance)
        {
            if (instance == null) return false;
            string name = instance.GetType().Name;
            
            // 明确排除远程同步模块，但允许MLDrawModule和ChainDrawModule
            if (name.Contains("Sync") && !name.Contains("Async")) 
            {
                if (name != "MLDrawModule" && name != "ChainDrawModule")
                {
                    return false; 
                }
            }
            
            // 明确允许ChainDrawModule（耳语模式）
            if (name == "ChainDrawModule")
            {
                 return false;
            }

            return false;
        }

        private static PropertyInfo _localPlayfabIdProp;
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        /// <summary>
        /// 开始绘制压力感应线条
        /// </summary>
        private static void StartPressureLine(DrawModule __instance, Vector2 startPoint)
        {
            try
            {
                var shapePrefab = DrawHelpers.Instance.FilledShape;
                var drawableShape = UnityEngine.Object.Instantiate(shapePrefab);
                
                // 获取所有者ID
                string owner = "local";
                try
                {
                    owner = LocalPlayfabIdProp?.GetValue(__instance) as string;
                    if (string.IsNullOrEmpty(owner)) owner = "local";
                }
                catch {}
                
                // 初始化绘制形状
                drawableShape.Init((byte)DrawModule.BrushSize.Value, __instance.GetActiveColorToUse(), __instance.SortOrder.GetNextSortOrder(false), DrawHelpers.GetSortingLayer(__instance.SelectedLayer, false, false), owner, __instance);
                
                // 确保Polygon组件存在
                if (drawableShape.Polygon == null)
                {
                    var poly = drawableShape.GetComponent<Polygon>();
                    if (poly == null)
                    {
                         DrawGuessPluginLoader.Log.LogError("StartPressureLine: 实例化的预制件缺少Polygon组件");
                         UnityEngine.Object.Destroy(drawableShape.gameObject);
                         return;
                    }
                }
                
                // 设置形状位置和父对象
                drawableShape.transform.position = Vector3.zero;
                var t = drawableShape.transform;
                t.SetParent(__instance.LineParent);
                var lp = t.localPosition;
                t.localPosition = new Vector3(lp.x, lp.y, __instance.SortOrder.NextLineZPos(false));
                
                // 创建压力线条创建器并开始新线条
                var creator = drawableShape.gameObject.AddComponent<PressureLineCreator>();
                creator.SetBrushSizeRange(1f, 99f);
                creator.SetDrawModule(__instance);
                creator.StartNewLine(startPoint, drawableShape);
                
                // 注册活跃创建器并更新DrawModule状态
                PressureLine.ActiveCreators[__instance] = creator;
                Traverse.Create(__instance).Field("lastPos").SetValue(startPoint);
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"StartPressureLine错误: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(DrawModule), "FinishLine", new Type[] { typeof(bool) })]
    public static class DrawModuleFinishLinePatch
    {
        [HarmonyPrefix]
        public static bool FinishLinePrefix(DrawModule __instance)
        {
            if (PressureLine.ActiveCreators.TryGetValue(__instance, out var creator))
            {
                creator.FinishLine();
                PressureLine.ActiveCreators.Remove(__instance);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DrawModule), "ExecuteClear")]
    public static class DrawModuleExecuteClearPatch
    {
        [HarmonyPrefix]
        public static bool ExecuteClearPrefix(DrawModule __instance, DrawingToolTrigger trigger, bool fromLocal, bool sync)
        {
            try
            {
                if (Bootstrap.Initialised && trigger != DrawingToolTrigger.System)
                    GameAnalyticsHandler.NewDesignEvent("DrawModule:ActivateTool:Clear");

                // 完成当前正在绘制的形状
                var shapeInProgress = Traverse.Create(__instance).Field("ShapeInProgress").GetValue<DrawableShape>();
                if (shapeInProgress != null && !shapeInProgress.ShapeCreator.Finished)
                    shapeInProgress.ShapeCreator.FinishShape();

                SelectiveFillableLine.Clear();

                // 收集所有需要清除的元素
                var myDrawnElements = __instance.GetMyDrawnElements();
                var elementsToClear = new HashSet<DrawableElement>(myDrawnElements);
                
                foreach (var group in PressureLine.PressureGroups.Values)
                {
                    foreach (var elem in group)
                    {
                        if (elem != null) elementsToClear.Add(elem);
                    }
                }

                var finalList = new List<DrawableElement>(elementsToClear);

                // 同步清除操作到网络
                if (sync)
                {
                    bool synced = false;
                    string instanceType = __instance.GetType().Name;
                    
                    // 处理LobbyDrawModule的同步
                    if (instanceType.Contains("LobbyDrawModule") && LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
                    {
                        try
                        {
                            string ownerId = AccessTools.Property(typeof(DrawModule), "LocalPlayfabId").GetValue(__instance) as string;
                            if (string.IsNullOrEmpty(ownerId)) ownerId = "local";
                            int layer = (int)AccessTools.Property(typeof(DrawModule), "SelectedLayer").GetValue(__instance);
                            LobbyManager.Instance.SyncController.LobbyDrawingClear(ownerId, layer);
                            synced = true;

                            // 备用：精确删除每个元素，确保同步完整性
                            foreach (var elem in finalList)
                            {
                                if (elem != null)
                                {
                                    string elemOwner = elem.Owner;
                                    if (string.IsNullOrEmpty(elemOwner)) elemOwner = ownerId;
                                    LobbyManager.Instance.SyncController.LobbyDrawingDeletePrecise(elemOwner, elem.Ident);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix手动同步错误: {ex}");
                        }
                    }

                    // 处理其他模块的同步
                    if (!synced)
                    {
                        try 
                        {
                            Traverse.Create(__instance).Method("OnSendSyncClear").GetValue();
                        }
                        catch (Exception ex)
                        {
                            DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix OnSendSyncClear错误: {ex}");
                        }
                    }
                }

                // 处理清除操作和撤销事件
                if (finalList.Count > 0)
                {
                    try 
                    {
                        var undoEvent = __instance.UndoSystem.CreateUndoClearElement(finalList);
                        __instance.UndoSystem.AddEvent(undoEvent);
                        var hub = Traverse.Create(__instance).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                        if (hub != null) hub.SetUndoButtons();
                    }
                    catch (Exception ex) 
                    {
                        DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix撤销错误: {ex}");
                    }

                    // 标记所有元素为删除状态
                    foreach (var elem in finalList)
                    {
                        if (elem != null)
                        {
                            elem.MarkAsDeleted(true);
                            if (elem.gameObject != null && elem.gameObject.activeSelf)
                            {
                                elem.gameObject.SetActive(false);
                            }
                        }
                    }

                    // 更新成就统计
                    if (trigger != DrawingToolTrigger.System)
                        DrawModuleAccoladeTracker.NrOfClears++;
                    if (trigger == DrawingToolTrigger.Shortcut)
                        DrawModuleAccoladeTracker.NrOfShortcutUses++;
                }

                // 更新UI状态并清除压力线条组
                var drawingToolHub = Traverse.Create(__instance).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                if (drawingToolHub != null) drawingToolHub.SetEraseButtons(false);

                Traverse.Create(__instance).Method("OnClear").GetValue();
                PressureLine.PressureGroups.Clear();

                return false;
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix错误: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(XORActivate), "OnDisable")]
    public static class XORActivateOnDisablePatch
    {
        [HarmonyPrefix]
        public static bool OnDisablePrefix(XORActivate __instance)
        {
            try
            {
                // 检查'Other'字段是否为null，避免空引用异常
                var otherField = AccessTools.Field(typeof(XORActivate), "Other");
                if (otherField != null)
                {
                    var otherObj = otherField.GetValue(__instance) as GameObject;
                    if (otherObj == null)
                    {
                        // 如果Other为null，则阻止原始方法运行
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                // 抑制错误，确保程序继续执行
                return false;
            }
            return true;
        }
    }
}