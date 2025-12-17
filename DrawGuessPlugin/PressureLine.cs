using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DrawGuessPlugin
{
    // PressureLine模块实现
    public class PressureLine : IDrawGuessPluginModule
    {
        internal static readonly Dictionary<DrawModule, PressureLineCreator> ActiveCreators = new();
        internal static readonly Dictionary<int, List<DrawableElement>> PressureGroups = new();

        internal static void RegisterSegment(int groupId, DrawableElement elem)
        {
            if (!PressureGroups.TryGetValue(groupId, out var list))
            {
                list = new List<DrawableElement>();
                PressureGroups[groupId] = list;
            }
            list.Add(elem);
        }

        internal static void ClearGroup(int groupId)
        {
            if (PressureGroups.ContainsKey(groupId)) PressureGroups.Remove(groupId);
        }
        public void Initialize(DrawGuessPluginLoader loader)
        {
            DrawGuessPluginLoader.Log.LogInfo("Initializing PressureLine module...");

            // 初始化线条组管理器
            var managerObj = new GameObject("PressureLineGroupManager");
            managerObj.AddComponent<PressureLineGroupManager>();
            UnityEngine.Object.DontDestroyOnLoad(managerObj);

            DrawGuessPluginLoader.Log.LogInfo("PressureLine module initialized successfully.");
        }

        public void Uninitialize()
        {
            DrawGuessPluginLoader.Log.LogInfo("Unloading PressureLine module...");
         
        }
    }

    public class PressureLineGroupManager : MonoBehaviour
    {
        public static PressureLineGroupManager Instance { get; private set; }

        private readonly Dictionary<int, List<DrawableLine>> lineGroups = new();
        private readonly Dictionary<DrawableLine, int> lineToGroupMap = new();
        private static int nextLineGroupID = 1;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DrawGuessPluginLoader.Log.LogInfo("PressureLineGroupManager initialized.");
        }

        public int CreateNewLineGroup(DrawableLine firstLine)
        {
            int groupID = nextLineGroupID++;
            lineGroups[groupID] = new List<DrawableLine> { firstLine };
            lineToGroupMap[firstLine] = groupID;
            return groupID;
        }

        public void AddLineToGroup(DrawableLine line, int groupID)
        {
            if (lineGroups.TryGetValue(groupID, out var lines))
            {
                lines.Add(line);
                lineToGroupMap[line] = groupID;
            }
        }

        public int GetLineGroupID(DrawableLine line) =>
            lineToGroupMap.TryGetValue(line, out int groupID) ? groupID : -1;

        public void DeleteSingleLine(DrawableLine line)
        {
            if (lineToGroupMap.TryGetValue(line, out int groupID))
            {
                lineGroups[groupID].Remove(line);
                lineToGroupMap.Remove(line);

                if (lineGroups[groupID].Count == 0)
                    lineGroups.Remove(groupID);
            }
        }

        public void DeleteEntireLineGroup(int groupID)
        {
            if (lineGroups.TryGetValue(groupID, out var lines))
            {
                foreach (var line in lines)
                    lineToGroupMap.Remove(line);

                lineGroups.Remove(groupID);
            }
        }

        public void ClearAllLineGroups()
        {
            lineGroups.Clear();
            lineToGroupMap.Clear();
        }
    }

    public class PressureLineSegment : MonoBehaviour
    {
        public int LineGroupID { get; set; }
        public float PressureValue { get; set; }
    }

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

    // 压感工具类，笔检查方法
    public static class PressureUtils
    {
        private static bool pressureSensitiveEnabled = true;

        public static bool IsPenAvailable() =>
            pressureSensitiveEnabled && Pen.current != null;
    }

    // 线条自相交检测和处理工具
    public static class LineIntersectionUtils
    {
        // 检测线段是否相交
        public static bool LineSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Direction(c, d, a);
            float d2 = Direction(c, d, b);
            float d3 = Direction(a, b, c);
            float d4 = Direction(a, b, d);

            // 标准相交情况
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && 
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            // 特殊共线情况（点在线段上）
            if (d1 == 0 && IsOnSegment(c, d, a)) return true;
            if (d2 == 0 && IsOnSegment(c, d, b)) return true;
            if (d3 == 0 && IsOnSegment(a, b, c)) return true;
            if (d4 == 0 && IsOnSegment(a, b, d)) return true;

            return false;
        }

        // 计算向量叉积方向
        private static float Direction(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
        }

        // 检测点是否在线段矩形范围内
        private static bool IsOnSegment(Vector2 p1, Vector2 p2, Vector2 p)
        {
            return p.x >= Mathf.Min(p1.x, p2.x) && p.x <= Mathf.Max(p1.x, p2.x) &&
                   p.y >= Mathf.Min(p1.y, p2.y) && p.y <= Mathf.Max(p1.y, p2.y);
        }

        // 检测多边形是否自相交
        public static bool CheckSelfIntersection(List<Vector2> points)
        {
            int count = points.Count;
            if (count < 4) return false;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 2; j < count; j++)
                {
                    // 跳过相邻的线段和首尾线段（如果是闭合多边形，首尾也算相邻）
                    if (Math.Abs(i - j) == 1 || (i == 0 && j == count - 1))
                        continue;

                    if (LineSegmentsIntersect(
                        points[i], 
                        points[(i + 1) % count], 
                        points[j], 
                        points[(j + 1) % count]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // 压感补丁类，包含所有与压感相关的补丁方法
    public static class PressurePatches
    {
        [HarmonyPatch(typeof(LinePressure))]
        [HarmonyPatch("Activate")]
        [HarmonyPrefix]
        public static bool LinePressureActivatePrefix(LinePressure __instance)
        {
            // 禁用游戏原有的LinePressure组件，接管压感处理
            __instance.enabled = false;
            return false; 
        }

        [HarmonyPatch(typeof(LinePressure))]
        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static bool LinePressureUpdatePrefix()
        {
            // 禁用LinePressure的Update方法
            return false; 
        }

        [HarmonyPatch(typeof(LinePressure))]
        [HarmonyPatch("Finish")]
        [HarmonyPostfix]
        public static void LinePressureFinishPostfix()
        {
            // 不需要重置currentLineGroupID，在每个线段创建时都会生成新的ID
        }

        // 将UpdateLineWidth方法移动到类内，作为公共静态方法
        public static void UpdateLineWidth(DrawableLine line, float pressure)
        {
            try
            {
                float adjustedPressure = Mathf.Clamp(pressure, 0.01f, 1f);
                const int minWidth = 2, maxWidth = 35;
                int calculatedWidth = minWidth + (int)((maxWidth - minWidth) * adjustedPressure);
                byte newWidth = (byte)Mathf.Clamp(calculatedWidth, minWidth, maxWidth);

                // 检查line是否为null
                if (line == null)
                {
                    DrawGuessPluginLoader.Log.LogError("UpdateLineWidth: line is null");
                    return;
                }

                // 更新BrushSize
                line.BrushSize = newWidth;

                // 安全地获取PolylineRenderer属性
                var polylineRendererProperty = line.GetType().GetProperty("PolylineRenderer");
                if (polylineRendererProperty == null)
                {
                    DrawGuessPluginLoader.Log.LogDebug("UpdateLineWidth: PolylineRenderer property not found");
                    return;
                }

                var polylineRenderer = polylineRendererProperty.GetValue(line);
                if (polylineRenderer == null)
                {
                    DrawGuessPluginLoader.Log.LogDebug("UpdateLineWidth: PolylineRenderer is null");
                    return;
                }

                // 安全地设置Thickness属性
                var thicknessProperty = polylineRenderer.GetType().GetProperty("Thickness");
                if (thicknessProperty == null)
                {
                    DrawGuessPluginLoader.Log.LogDebug("UpdateLineWidth: Thickness property not found");
                    return;
                }

                
                float thickness = (float)newWidth / 100f;
                thicknessProperty.SetValue(polylineRenderer, thickness);

                var colorProperty = polylineRenderer.GetType().GetProperty("Color");
                if (colorProperty != null)
                {
                    Color currentColor = (Color)colorProperty.GetValue(polylineRenderer);
                    colorProperty.SetValue(polylineRenderer, currentColor);
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"UpdateLineWidth exception: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    [HarmonyPatch(typeof(DrawModule), "ActivateTool")]
        public static class DrawModuleActivateToolPatch
        {
            [HarmonyPostfix]
            public static void ActivateToolPostfix(DrawModule __instance, DrawModule.DrawTool tool)
            {
                // 当工具切换时，强制更新所有压感线条的组件状态
                // 确保它们响应新的工具（Eraser, Fill, Dropper）
                
                try
                {
                    DrawGuessPluginLoader.Log.LogInfo($"ActivateToolPostfix: Tool changed to {tool}. Updating components.");
                    
                    
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
                                else
                                {

                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DrawGuessPluginLoader.Log.LogError($"ActivateToolPostfix Error: {ex}");
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
                    DrawGuessPluginLoader.Log.LogInfo($"DeletedPrecise: Detected deletion of segment in group {groupId}. Triggering cascade delete.");
                    
                    var groupElements = new List<DrawableElement>(PressureLine.PressureGroups[groupId]);
                    
                    // 添加组合 Undo
                    __instance.UndoSystem.AddEvent(new UndoMultiEraseElement(groupElements));
                    // 使用 AccessTools 访问受保护的成员
                    var drawingToolHub = AccessTools.Field(typeof(DrawModule), "DrawingToolHub").GetValue(__instance) as DrawingToolHub;
                    if (drawingToolHub != null) drawingToolHub.SetUndoButtons();

                    // 标记删除
                    foreach (var elem in groupElements)
                    {
                        if (elem != null)
                        {
                            elem.MarkAsDeleted(true);
                            // 级联更新 UI 状态
                            // OnSendDeletePrecise 是 protected，使用 Traverse 调用
                            Traverse.Create(__instance).Method("OnSendDeletePrecise", new object[] { elem.Ident }).GetValue();
                        }
                    }
                    
                    
                    // 返回 false 拦截原逻辑，避免只删除选中的那一段
                    return false;
                }
                
                return true;
            }
        }

    public class UndoMultiEraseElement : IUndo
    {
        private List<DrawableElement> lines;

        public UndoMultiEraseElement(IEnumerable<DrawableElement> lines)
        {
            this.lines = new List<DrawableElement>(lines);
        }

        public void Undo()
        {
            // Undo Erase -> Restore (Show)
            foreach (var line in lines)
            {
                if (line != null) line.MarkAsDeleted(false);
            }
        }

        public void Redo()
        {
            // Redo Erase -> Delete (Hide)
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
            // 如果未启用压感，或当前工具不是笔刷，则执行原逻辑
            if (!MenuGameSettings.UsePenPressure.Value) return true;
            if (__instance.ActiveDrawTool != DrawModule.DrawTool.Brush) return true;

            // 检查是否为 Stage 模式 (MLDrawModule) 或 远程同步模块 (SyncDrawModule)
            // 如果是，则跳过压感逻辑，使用原逻辑
            if (IsStageOrSyncModule(__instance))
            {
                return true;
            }

            // 获取输入状态
            bool primaryDown = DrawInput.GetPrimaryDown();
            bool primary = DrawInput.GetPrimary();
            bool primaryUp = DrawInput.GetPrimaryUp();

            // 处理 Finish (鼠标/笔抬起)
            if (primaryUp)
            {
                if (PressureLine.ActiveCreators.TryGetValue(__instance, out var creator))
                {
                    creator.FinishLine();
                    PressureLine.ActiveCreators.Remove(__instance);
                    return false; // 拦截原逻辑
                }
            }

            // 处理 Start (鼠标/笔按下)
            if (primaryDown)
            {
                Vector2 worldPoint;
                // 必须同时满足：在绘图区域内，且直接在绘图表面上（未被UI遮挡）
                if (__instance.GetWorldCoordsIfOverDrawSurface(out worldPoint) && DrawModule.IsDirectlyOverDrawSurface())
                {
                    StartPressureLine(__instance, worldPoint);
                    return false; // 拦截原逻辑
                }
            }

            // 处理 Append (鼠标/笔按住)
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
                    return false; // 拦截原逻辑
                }
            }

            return true; // 如果没有任何压感操作匹配，回退到原逻辑
        }

        private static readonly Dictionary<Type, bool> _moduleTypeCache = new Dictionary<Type, bool>();
        private static bool IsStageOrSyncModule(DrawModule instance)
        {
            if (instance == null) return false;
            var type = instance.GetType();
            if (_moduleTypeCache.TryGetValue(type, out bool result)) return result;

            string name = type.Name;
            
            // 显式排除 SyncDrawModule (远程同步)
            if (name.Contains("Sync") || name.Contains("SyncDrawModule"))
            {
                _moduleTypeCache[type] = true;
                return true;
            }

            // 显式排除 MLDrawModule (Stage / 游戏内)
            if (name.Contains("MLDrawModule") || name.Contains("Stage") || name.Contains("ML"))
            {
                _moduleTypeCache[type] = true;
                return true;
            }

            try
            {
                // Duck Typing: MLDrawModule 独有 'player' 和 'timer' 字段
                // 使用标准反射避免 Harmony AccessTools.Field 产生警告
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (type.GetField("player", flags) != null && type.GetField("timer", flags) != null)
                {
                    _moduleTypeCache[type] = true;
                    return true;
                }
            }
            catch {}
            
            _moduleTypeCache[type] = false;
            return false;
        }

        private static PropertyInfo _localPlayfabIdProp;
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        private static void StartPressureLine(DrawModule __instance, Vector2 startPoint)
        {
            try
            {
                DrawGuessPluginLoader.Log.LogInfo($"StartPressureLine: Starting at {startPoint}");
                var shapePrefab = DrawHelpers.Instance.FilledShape;
                var drawableShape = UnityEngine.Object.Instantiate(shapePrefab);
                string owner = null;
                try
                {
                    // 使用 AccessTools 获取属性
                    owner = LocalPlayfabIdProp?.GetValue(__instance) as string;
                }
                catch { }
                
                if (string.IsNullOrEmpty(owner))
                {
                    owner = "local";
                }

                drawableShape.Init((byte)DrawModule.BrushSize.Value, __instance.GetActiveColorToUse(), __instance.SortOrder.GetNextSortOrder(false), DrawHelpers.GetSortingLayer(__instance.SelectedLayer, false, false), owner, __instance);
                drawableShape.transform.position = Vector3.zero;
                var t = drawableShape.transform;
                t.SetParent(__instance.LineParent);
                var lp = t.localPosition;
                t.localPosition = new Vector3(lp.x, lp.y, __instance.SortOrder.NextLineZPos(false));

                var creator = drawableShape.gameObject.AddComponent<PressureLineCreator>();
                creator.SetBrushSizeRange(1f, 99f);
                creator.SetDrawModule(__instance);
                creator.StartNewLine(startPoint, drawableShape);
                
                PressureLine.ActiveCreators[__instance] = creator;
                
                // 更新 lastPos
                Traverse.Create(__instance).Field("lastPos").SetValue(startPoint);
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"StartPressureLine Error: {ex}");
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

    // 线条完成后处理自相交情况
    [HarmonyPatch(typeof(DrawModule), "FinishLine", new Type[] { typeof(bool) })]
    public static class DrawModuleFinishLineIntersectionPatch
    {
        [HarmonyPrefix]
        public static void FinishLinePrefix(DrawModule __instance, bool addFinishingPoint)
        {
            var traverse = Traverse.Create(__instance);
            var currentLine = traverse.Field("LineInProgress").GetValue<DrawableLine>();
            
            if (currentLine != null)
            {
                try
                {
                    // 获取当前线条的所有点
                    var polylineRenderer = currentLine.PolylineRenderer;
                    if (polylineRenderer != null && polylineRenderer.points.Count > 3)
                    {
                        // 提取点列表
                        List<Vector2> points = new List<Vector2>();
                        foreach (var point in polylineRenderer.points)
                        {
                            points.Add(point.point);
                        }
                        
                        // 检测自相交
                        if (LineIntersectionUtils.CheckSelfIntersection(points))
                        {

                        }
                    }
                }
                catch (Exception ex)
                {
                    DrawGuessPluginLoader.Log.LogError($"Self-intersection detection error: {ex.Message}");
                }
            }
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

                var shapeInProgress = Traverse.Create(__instance).Field("ShapeInProgress").GetValue<DrawableShape>();
                if (shapeInProgress != null && !shapeInProgress.ShapeCreator.Finished)
                    shapeInProgress.ShapeCreator.FinishShape();

                SelectiveFillableLine.Clear();

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

                
                if (sync)
                {
                    bool synced = false;
                    string instanceType = __instance.GetType().Name;
                    
                  
                    if (instanceType.Contains("LobbyDrawModule") && LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
                    {
                        try
                        {
                            string ownerId = AccessTools.Property(typeof(DrawModule), "LocalPlayfabId").GetValue(__instance) as string;
                            if (string.IsNullOrEmpty(ownerId)) ownerId = "local";
                            int layer = (int)AccessTools.Property(typeof(DrawModule), "SelectedLayer").GetValue(__instance);
                            LobbyManager.Instance.SyncController.LobbyDrawingClear(ownerId, layer);
                            synced = true;

                           
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
                            DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix Manual Sync Error: {ex}");
                        }
                    }

                    if (!synced)
                    {
                       
                        try 
                        {
                            Traverse.Create(__instance).Method("OnSendSyncClear").GetValue();
                        }
                        catch (Exception ex)
                        {
                            DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix OnSendSyncClear Error: {ex}");
                        }
                    }
                }

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
                        DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix Undo Error: {ex}");
                    }

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

                    if (trigger != DrawingToolTrigger.System)
                        DrawModuleAccoladeTracker.NrOfClears++;
                    if (trigger == DrawingToolTrigger.Shortcut)
                        DrawModuleAccoladeTracker.NrOfShortcutUses++;
                }

                var drawingToolHub = Traverse.Create(__instance).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                if (drawingToolHub != null) drawingToolHub.SetEraseButtons(false);

                Traverse.Create(__instance).Method("OnClear").GetValue();

                PressureLineGroupManager.Instance.ClearAllLineGroups();
                PressureLine.PressureGroups.Clear();

                return false;
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix Error: {ex}");
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
                
                var otherField = AccessTools.Field(typeof(XORActivate), "Other");
                if (otherField != null)
                {
                    var otherObj = otherField.GetValue(__instance) as GameObject;
                    if (otherObj == null)
                    {
                        
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                
                return false;
            }
            return true;
        }
    }
}

