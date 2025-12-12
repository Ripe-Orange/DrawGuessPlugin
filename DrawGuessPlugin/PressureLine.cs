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
            DrawGuessPluginLoader.Log.LogInfo("正在初始化PressureLine模块...");

            // 初始化Harmony补丁
            DrawGuessPluginLoader.Log.LogInfo("PressureLine模块初始化成功.");
        }

        public void Uninitialize()
        {
            DrawGuessPluginLoader.Log.LogInfo("正在卸载PressureLine模块...");
            PressureGroups.Clear();
            ActiveCreators.Clear();
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
                if (line != null) line.MarkAsDeleted(false);
            }
        }

        public void Undo()
        {
            foreach (var line in lines)
            {
                if (line != null) line.MarkAsDeleted(true);
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
                // 优化：跳过首尾相接的检查，只检查不相邻的线段
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

        private static PropertyInfo _polylineRendererProp;
        private static PropertyInfo _thicknessProp;
        private static PropertyInfo _colorProp;

        // 将UpdateLineWidth方法移动到类内，作为公共静态方法
        public static void UpdateLineWidth(DrawableLine line, float pressure)
        {
            try
            {
                if (line == null) return;

                float adjustedPressure = Mathf.Clamp(pressure, 0.01f, 1f);
                const int minWidth = 2, maxWidth = 35;
                int calculatedWidth = minWidth + (int)((maxWidth - minWidth) * adjustedPressure);
                byte newWidth = (byte)Mathf.Clamp(calculatedWidth, minWidth, maxWidth);

                // 更新BrushSize
                line.BrushSize = newWidth;

                // 缓存反射属性以提高性能
                if (_polylineRendererProp == null)
                    _polylineRendererProp = line.GetType().GetProperty("PolylineRenderer");
                
                if (_polylineRendererProp == null) return;

                var polylineRenderer = _polylineRendererProp.GetValue(line);
                if (polylineRenderer == null) return;

                if (_thicknessProp == null)
                    _thicknessProp = polylineRenderer.GetType().GetProperty("Thickness");
                
                if (_thicknessProp == null) return;
                
                float thickness = (float)newWidth / 100f;
                _thicknessProp.SetValue(polylineRenderer, thickness);

                if (_colorProp == null)
                    _colorProp = polylineRenderer.GetType().GetProperty("Color");

                if (_colorProp != null)
                {
                    Color currentColor = (Color)_colorProp.GetValue(polylineRenderer);
                    _colorProp.SetValue(polylineRenderer, currentColor);
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"UpdateLineWidth exception: {ex.Message}");
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
                // 检测到删除属于某组的线段，触发级联删除
                var groupElements = new List<DrawableElement>(PressureLine.PressureGroups[groupId]);
                
                // 1. 添加组合 Undo
                __instance.UndoSystem.AddEvent(new UndoMultiEraseElement(groupElements));
                
                var drawingToolHub = AccessTools.Field(typeof(DrawModule), "DrawingToolHub").GetValue(__instance) as DrawingToolHub;
                if (drawingToolHub != null) drawingToolHub.SetUndoButtons();

                // 2. 标记删除
                foreach (var elem in groupElements)
                {
                    if (elem != null)
                    {
                        elem.MarkAsDeleted(true);
                        // 级联更新 UI 状态
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
                if (line != null) line.MarkAsDeleted(true);
            }
        }
    }

    [HarmonyPatch(typeof(DrawModule), "OnUpdateTick")]
    public static class DrawModuleOnUpdateTickPatch
    {
        [HarmonyPrefix]
        public static bool OnUpdateTickPrefix(DrawModule __instance)
        {
            // 1. 如果未启用压感，或当前工具不是笔刷，则执行原逻辑
            if (!MenuGameSettings.UsePenPressure.Value) return true;
            if (__instance.ActiveDrawTool != DrawModule.DrawTool.Brush) return true;

            // 2. 获取输入状态
            bool primaryDown = DrawInput.GetPrimaryDown();
            bool primary = DrawInput.GetPrimary();
            bool primaryUp = DrawInput.GetPrimaryUp();

            // 3. 处理 Finish (鼠标/笔抬起)
            if (primaryUp)
            {
                if (PressureLine.ActiveCreators.TryGetValue(__instance, out var creator))
                {
                    creator.FinishLine();
                    PressureLine.ActiveCreators.Remove(__instance);
                    return false; // 拦截原逻辑
                }
            }

            // 4. 处理 Start (鼠标/笔按下)
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

            // 5. 处理 Append (鼠标/笔按住)
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

        private static PropertyInfo _localPlayfabIdProp;
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        private static void StartPressureLine(DrawModule __instance, Vector2 startPoint)
        {
            try
            {
                DrawGuessPluginLoader.Log.LogInfo($"StartPressureLine: Starting at {startPoint}");
                var shapePrefab = DrawHelpers.Instance.FilledShape;
                
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

                // 使用对象池获取 Shape
                var drawableShape = DrawableShapePool.Get(
                    shapePrefab, 
                    __instance, 
                    (byte)DrawModule.BrushSize.Value, 
                    __instance.GetActiveColorToUse(), 
                    __instance.SortOrder.GetNextSortOrder(false), 
                    DrawHelpers.GetSortingLayer(__instance.SelectedLayer, false, false), 
                    owner
                );
                
                // 添加Creator组件
                var creator = drawableShape.gameObject.GetComponent<PressureLineCreator>();
                if (creator == null) creator = drawableShape.gameObject.AddComponent<PressureLineCreator>();
                
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
                            DrawGuessPluginLoader.Log.LogDebug($"检测到线条自相交，点数: {points.Count}");
                            
                        }
                    }
                }
                catch (Exception ex)
                {
                    DrawGuessPluginLoader.Log.LogError($"自相交检测异常: {ex.Message}");
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
                // 完全接管 ExecuteClear 逻辑
                // 1. 基础检查和准备
                if (Bootstrap.Initialised && trigger != DrawingToolTrigger.System)
                    GameAnalyticsHandler.NewDesignEvent("DrawModule:ActivateTool:Clear");

                // 2. 结束正在进行的形状
                var shapeInProgress = Traverse.Create(__instance).Field("ShapeInProgress").GetValue<DrawableShape>();
                if (shapeInProgress != null && !shapeInProgress.ShapeCreator.Finished)
                    shapeInProgress.ShapeCreator.FinishShape();

                // 3. 清理填充线
                SelectiveFillableLine.Clear();

                // 4. 获取所有需要清除的元素 (合并游戏列表和我们的压感列表)
                var myDrawnElements = __instance.GetMyDrawnElements();
                var elementsToClear = new HashSet<DrawableElement>(myDrawnElements);
                
                // 从 PressureGroups 中补充
                foreach (var group in PressureLine.PressureGroups.Values)
                {
                    foreach (var elem in group)
                    {
                        if (elem != null) elementsToClear.Add(elem);
                    }
                }

                var finalList = new List<DrawableElement>(elementsToClear);

                // 5. 执行清除逻辑
                if (finalList.Count > 0)
                {
                    DrawGuessPluginLoader.Log.LogInfo($"ExecuteClearPrefix: Clearing {finalList.Count} elements.");

                    // 添加 Undo
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

                    // 标记删除
                    foreach (var elem in finalList)
                    {
                        if (elem != null)
                        {
                            elem.MarkAsDeleted(true);
                            // 强制隐藏，确保视觉上消失
                            if (elem.gameObject != null && elem.gameObject.activeSelf)
                            {
                                elem.gameObject.SetActive(false);
                            }
                        }
                    }

                    // 记录成就
                    if (trigger != DrawingToolTrigger.System)
                        DrawModuleAccoladeTracker.NrOfClears++;
                    if (trigger == DrawingToolTrigger.Shortcut)
                        DrawModuleAccoladeTracker.NrOfShortcutUses++;
                }

                // 6. 更新 UI 按钮状态
                var drawingToolHub = Traverse.Create(__instance).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                if (drawingToolHub != null) drawingToolHub.SetEraseButtons(false);

                // 7. 同步
                if (sync)
                {
                    // 调用 protected OnSendSyncClear
                    Traverse.Create(__instance).Method("OnSendSyncClear").GetValue();
                }

                // 8. 调用 OnClear
                Traverse.Create(__instance).Method("OnClear").GetValue();

                // 9. 清理我们的组管理器
                // 9. Clear our group manager
                PressureLine.PressureGroups.Clear();

                return false; // 拦截原方法
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"ExecuteClearPrefix Error: {ex}");
                return true; // 出错则回退到原方法
            }
        }
    }
}

