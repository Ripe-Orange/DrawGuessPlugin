using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Shapes;

namespace DrawGuessPlugin
{
    /// <summary>
    /// 压力感应线条创建器，用于生成带有压力变化的线条
    /// </summary>
    public class PressureLineCreator : MonoBehaviour
    {
        [SerializeField] private float minPointDistanceMin = 0.01f;
        [SerializeField] private float minPointDistanceMax = 0.07f;
        [SerializeField] private int segmentSizeMin = 10;
        [SerializeField] private int segmentSizeMax = 35;

        private DrawModule drawModule;
        private readonly List<DrawableShape> drawElements = new List<DrawableShape>();
        private DrawableShape currentDrawElement;
        private Polygon currentPolygon;
        private readonly List<Vector2> mainPoints = new List<Vector2>();
        private readonly List<float> brushSizes = new List<float>();
        private readonly List<Vector2> currentSegmentPoints = new List<Vector2>();
        private readonly List<float> currentSegmentBrushSizes = new List<float>();
        private SmoothedValue pressureSmoother;
        private float minBrushSize = 1f;
        private float maxBrushSize = 99f;
        private readonly List<DrawableShape> fullLineSegments = new List<DrawableShape>();
        private int lineGroupID = -1;
        private static int lastLineGroupID;

        // Reflection缓存，用于优化反射调用性能
        private static FieldInfo _createdDrawablesField;
        private static FieldInfo CreatedDrawablesField => _createdDrawablesField ??= AccessTools.Field(typeof(DrawModule), "CreatedDrawablesByMe");
        
        private static FieldInfo _existingLinesField;
        private static PropertyInfo _localPlayfabIdProp;
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        private static bool _checkedForMlModule = false;
        private static bool _isMlModule = false;

        public bool Finished { get; private set; }
        public int LineGroupID => lineGroupID;

        /// <summary>
        /// 设置笔刷大小范围
        /// </summary>
        public void SetBrushSizeRange(float min, float max)
        {
            minBrushSize = min;
            maxBrushSize = max;
        }

        /// <summary>
        /// 设置绘图模块引用
        /// </summary>
        public void SetDrawModule(DrawModule dm)
        {
            drawModule = dm;
            pressureSmoother = new SmoothedValue(5);
            CheckModuleType();
        }

        /// <summary>
        /// 检查当前绘图模块类型，确定是否为ML模块
        /// </summary>
        private void CheckModuleType()
        {
            if (drawModule == null) return;
            
            if (!_checkedForMlModule)
            {
                var typeName = drawModule.GetType().Name;
                
                if (typeName.Contains("Sync") || typeName == "MLDrawModule" || typeName.Contains("Stage") || typeName.Contains("ML") || typeName == "ChainDrawModule")
                {
                    _isMlModule = true;
                }
                _checkedForMlModule = true;
            }
        }

        /// <summary>
        /// 更新橡皮擦按钮状态
        /// </summary>
        private void UpdateEraseButtons()
        {
            try
            {
                var hub = Traverse.Create(drawModule).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                if (hub != null)
                {
                    hub.SetEraseButtons(true);
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"更新橡皮擦按钮失败: {ex}");
            }
        }

        /// <summary>
        /// 将绘图元素添加到创建列表中
        /// </summary>
        private void AddToCreatedDrawables(DrawableElement elem)
        {
            try
            {
                var collection = CreatedDrawablesField?.GetValue(drawModule) as System.Collections.ObjectModel.ObservableCollection<DrawableElement>;
                if (collection != null && !collection.Contains(elem))
                {
                    collection.Add(elem);
                }

                AddToLobbyExistingLines(elem);
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"添加到创建列表失败: {ex}");
            }
        }

        /// <summary>
        /// 将绘图元素添加到大厅现有线条列表中（仅非ML模块）
        /// </summary>
        private void AddToLobbyExistingLines(DrawableElement elem)
        {
            if (_isMlModule) return;

            try
            {
                if (_existingLinesField == null)
                {
                     _existingLinesField = drawModule.GetType().GetField("ExistingLinesInMulti", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (_existingLinesField == null) return;

                var existingLines = _existingLinesField.GetValue(drawModule) as System.Collections.IDictionary;
                
                if (existingLines != null)
                {
                    string ownerId = elem.Owner;
                    if (string.IsNullOrEmpty(ownerId))
                    {
                        ownerId = LocalPlayfabIdProp?.GetValue(drawModule) as string;
                    }

                    if (!string.IsNullOrEmpty(ownerId))
                    {
                        if (!existingLines.Contains(ownerId))
                        {
                            var newDict = Activator.CreateInstance(typeof(Dictionary<int, DrawableElement>));
                            existingLines.Add(ownerId, newDict);
                        }

                        var innerDict = existingLines[ownerId] as System.Collections.IDictionary;
                        if (innerDict != null && !innerDict.Contains(elem.Ident))
                        {
                            innerDict.Add(elem.Ident, elem);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 静默失败，可能不是Lobby模块
            }
        }

        /// <summary>
        /// 开始新线条绘制（非ML模块）
        /// </summary>
        public void StartNewLine(Vector2 startPoint, DrawableShape drawableShape)
        {
            // if (_isMlModule) return; // REMOVED: This was blocking initialization in Sync/Stage modes

            lineGroupID = ++lastLineGroupID;
            fullLineSegments.Clear();
            fullLineSegments.Add(drawableShape);
            
            PressureLine.RegisterSegment(lineGroupID, drawableShape);
            AddToCreatedDrawables(drawableShape);
            UpdateEraseButtons();
            drawableShape.SetHoverActive(true);
            
            currentDrawElement = drawableShape;
            currentPolygon = currentDrawElement.Polygon ?? currentDrawElement.GetComponent<Polygon>();
            if (currentPolygon == null)
            {
                DrawGuessPluginLoader.Log.LogError("StartNewLine: 无法找到Polygon组件");
                return;
            }

            ResetLineData();
            Finished = false;
            enabled = true;

            mainPoints.Add(startPoint);
            float initialPressure = Pen.current != null ? Pen.current.pressure.ReadValue() : 1f;
            brushSizes.Add(Mathf.Lerp(1f, 99f, initialPressure));
            currentSegmentPoints.Add(startPoint);
            currentSegmentBrushSizes.Add(brushSizes[0]);
        }

        /// <summary>
        /// 开始绘制线条（ML模块）
        /// </summary>
        public void BeginLine(DrawableShape drawableShape)
        {
            lineGroupID = ++lastLineGroupID;
            fullLineSegments.Clear();
            fullLineSegments.Add(drawableShape);
            
            PressureLine.RegisterSegment(lineGroupID, drawableShape);
            AddToCreatedDrawables(drawableShape);
            UpdateEraseButtons();
            drawableShape.SetHoverActive(true);
            
            currentDrawElement = drawableShape;
            currentPolygon = currentDrawElement.Polygon ?? currentDrawElement.GetComponent<Polygon>();
            if (currentPolygon == null)
            {
                DrawGuessPluginLoader.Log.LogError("BeginLine: 无法找到Polygon组件");
                return;
            }

            ResetLineData();
            Finished = false;
            enabled = true;
        }

        /// <summary>
        /// 重置线条数据，用于开始新线条或清除现有数据
        /// </summary>
        private void ResetLineData()
        {
            drawElements.Clear();
            mainPoints.Clear();
            brushSizes.Clear();
            currentSegmentPoints.Clear();
            currentSegmentBrushSizes.Clear();
        }

        /// <summary>
        /// 添加点到当前线条，根据压力调整笔刷大小
        /// </summary>
        public void AppendPoint(Vector2 worldPoint, float rawPressure)
        {
            if (drawModule == null || Finished) return;

            // 处理压力值，应用平滑和映射
            float clamped = Mathf.Clamp(rawPressure, 0.001f, 1f);
            pressureSmoother.AddValue((int)(clamped * 1000f));
            float smoothed = pressureSmoother.SmoothValue(5) / 1000f;
            float t = smoothed < 0.15f ? 0f : (smoothed - 0.15f) / 0.85f;
            float brushSize = Mathf.Lerp(minBrushSize, maxBrushSize, t);
            
            // 获取动态参数
            float minPointDistance = GetDynamicMinPointDistance(brushSize);
            int dynamicSegmentSize = GetDynamicSegmentSize(brushSize);

            if (mainPoints.Count == 0)
            {
                AddFirstPoint(worldPoint, brushSize);
                return;
            }

            if (Vector2.Distance(worldPoint, mainPoints[^1]) < minPointDistance) return;

            // 添加新点
            mainPoints.Add(worldPoint);
            brushSizes.Add(brushSize);
            currentSegmentPoints.Add(worldPoint);
            currentSegmentBrushSizes.Add(brushSize);

            // 更新或结束当前线段
            if (currentSegmentPoints.Count < dynamicSegmentSize)
            {
                UpdateCurrentPolygon();
            }
            else
            {
                FinalizeCurrentSegment();
                StartNewSegment();
            }
        }

        /// <summary>
        /// 添加第一个点到线条
        /// </summary>
        private void AddFirstPoint(Vector2 worldPoint, float brushSize)
        {
            mainPoints.Add(worldPoint);
            brushSizes.Add(brushSize);
            currentSegmentPoints.Add(worldPoint);
            currentSegmentBrushSizes.Add(brushSize);
        }

        /// <summary>
        /// 完成线条绘制
        /// </summary>
        public void FinishLine()
        {
            if (mainPoints.Count < 2)
            {
                if (currentDrawElement != null) Destroy(currentDrawElement.gameObject);
                return;
            }

            Finished = true;
            if (currentSegmentPoints.Count > 1)
            {
                UpdateCurrentPolygon();
                FinalizeCurrentSegment();
            }

            // 处理撤销事件
            var list = fullLineSegments.Where(seg => seg != null).Cast<DrawableElement>().ToList();
            if (list.Count > 0)
            {
                drawModule.UndoSystem.AddEvent(new UndoMultiDrawElement(list));
                
                try
                {
                    var hub = Traverse.Create(drawModule).Field("DrawingToolHub").GetValue<DrawingToolHub>();
                    if (hub != null) hub.SetUndoButtons();
                }
                catch {}
            }

            enabled = false;
        }

        /// <summary>
        /// 根据笔刷大小获取动态最小点距离，控制线条密度
        /// </summary>
        private float GetDynamicMinPointDistance(float brushSize)
        {
            if (brushSize <= 6f) return minPointDistanceMin;
            float t = (brushSize - 6f) / (maxBrushSize - 6f);
            return Mathf.Lerp(minPointDistanceMin, minPointDistanceMax, t);
        }

        /// <summary>
        /// 根据笔刷大小获取动态线段大小，控制线段长度
        /// </summary>
        private int GetDynamicSegmentSize(float brushSize)
        {
            if (brushSize <= 2f) return 4;
            float t = (brushSize - 2f) / (maxBrushSize - 2f);
            return Mathf.RoundToInt(Mathf.Lerp(segmentSizeMin, segmentSizeMax, t));
        }

        /// <summary>
        /// 开始新线段
        /// </summary>
        private void StartNewSegment()
        {
            var lastPoint = currentSegmentPoints[^1];
            var lastBrush = currentSegmentBrushSizes[^1];
            currentDrawElement = CreateNewPressureSegment(lastPoint, lastBrush, drawModule);
            
            if (currentDrawElement == null)
            {
                DrawGuessPluginLoader.Log.LogError("StartNewSegment: 创建新线段失败，终止线条绘制");
                Finished = true;
                return;
            }

            currentPolygon = currentDrawElement.Polygon ?? currentDrawElement.GetComponent<Polygon>();
            if (currentPolygon == null)
            {
                DrawGuessPluginLoader.Log.LogError("StartNewSegment: 无法找到Polygon组件，终止绘制");
                Finished = true;
                return;
            }

            currentSegmentPoints.Clear();
            currentSegmentBrushSizes.Clear();
            currentSegmentPoints.Add(lastPoint);
            currentSegmentBrushSizes.Add(lastBrush);
        }

        /// <summary>
        /// 完成当前线段，处理自相交情况
        /// </summary>
        private void FinalizeCurrentSegment()
        {
            if (currentSegmentPoints.Count < 2) return;
            
            var points = GeneratePolygonPoints(false);
            if (CheckSelfIntersection(points))
            {
                HandleSelfIntersection();
                return;
            }
            FinalizeCurrentSegmentInternal();
        }

        /// <summary>
        /// 内部完成线段逻辑，不处理自相交
        /// </summary>
        private void FinalizeCurrentSegmentInternal()
        {
            if (currentPolygon == null) return;
            
            var polyPoints = GeneratePolygonPoints(true);
            if (polyPoints == null || polyPoints.Count == 0) return;
            
            currentPolygon.SetPoints(polyPoints);
            var colliderPoints = currentPolygon.points.ToArray();
            currentDrawElement.SetColliderPoints(colliderPoints);
            drawElements.Add(currentDrawElement);
            drawModule.CheckSpecialMode(currentDrawElement, true);
            
            drawModule.SortOrder.IncreaseStrokeCount(currentDrawElement.DrawType == DrawElementT.Fill);
            
            PressureLine.RegisterSegment(lineGroupID, currentDrawElement);
            fullLineSegments.Add(currentDrawElement);
            
            // 网络同步逻辑
            SyncCurrentSegment();
        }

        /// <summary>
        /// 同步当前线段到网络，支持不同游戏模式
        /// </summary>
        private void SyncCurrentSegment()
        {
            try
            {
                string moduleName = drawModule.GetType().Name;
                
                // 1. Stage模式 (MLDrawModule / SyncDrawModule)
                if (_isMlModule && (moduleName == "MLDrawModule" || moduleName == "SyncDrawModule"))
                {
                    SyncStageMode();
                    return;
                }

                // 2. 耳语/链式模式 (ChainDrawModule)
                if (moduleName == "ChainDrawModule")
                {
                    SyncChainMode();
                    return;
                }

                // 3. 大厅模式
                if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
                {
                    var lineInfo = currentDrawElement.ToLineInformation();
                    LobbyManager.Instance.SyncController.LobbyAddDrawLine(lineInfo);
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"SyncCurrentSegment错误: {ex}");
            }
        }

        /// <summary>
        /// 同步Stage模式下的线段
        /// </summary>
        private void SyncStageMode()
        {
            var playerField = AccessTools.Field(drawModule.GetType(), "player");
            if (playerField == null) return;
            
            var player = playerField.GetValue(drawModule);
            if (player == null) return;
            
            string playerTypeName = player.GetType().Name;
            
            // 1. 同步猜测模式 (SyncDGPlayer)
            if (playerTypeName == "SyncDGPlayer")
            {
                var addLineMethod = AccessTools.Method(player.GetType(), "DrawAddLine", new Type[] { typeof(LineInformation) });
                if (addLineMethod != null)
                {
                    var lineInfo = currentDrawElement.ToLineInformation();
                    addLineMethod.Invoke(player, new object[] { lineInfo });
                }
            }
            // 2. 舞台模式 (MLDGPlayer)
            else 
            {
                var distributeMethod = AccessTools.Method(player.GetType(), "DistributeDrawingPartial", new Type[] { typeof(DrawableElement) });
                if (distributeMethod != null)
                {
                    distributeMethod.Invoke(player, new object[] { currentDrawElement });
                }
            }
        }

        /// <summary>
        /// 同步链式模式下的线段
        /// </summary>
        private void SyncChainMode()
        {
            var chainPlayerType = AccessTools.TypeByName("ChainDGPlayer");
            if (chainPlayerType == null) return;
            
            var localPlayerProp = AccessTools.Property(chainPlayerType, "ChainLocalPlayer");
            if (localPlayerProp == null) return;
            
            var localPlayer = localPlayerProp.GetValue(null);
            if (localPlayer == null) return;
            
            var distributeMethod = AccessTools.Method(chainPlayerType, "DistributeDrawingPartial", new Type[] { typeof(DrawableElement) });
            if (distributeMethod != null)
            {
                distributeMethod.Invoke(localPlayer, new object[] { currentDrawElement });
            }
        }

        // 多边形点缓冲区，用于避免频繁创建列表
        private readonly List<Vector2> polygonPointsBuffer = new List<Vector2>(1024);

        /// <summary>
        /// 生成多边形点，用于创建带有压力变化的线条形状
        /// </summary>
        private List<Vector2> GeneratePolygonPoints(bool isFinalizing)
        {
            polygonPointsBuffer.Clear();
            var list = polygonPointsBuffer;
            bool prependPrev = list.Count == 0 && drawElements.Count > 0;
            
            if (!prependPrev) list.Add(currentSegmentPoints[0]);
            
            for (int i = 1; i < currentSegmentPoints.Count; i++)
            {
                bool last = i == currentSegmentPoints.Count - 1;
                
                if (last && Finished)
                {
                    // 处理线条末端
                    var a = currentSegmentPoints[i - 1];
                    var b = currentSegmentPoints[i];
                    var dir = (b - a).normalized;
                    var n = new Vector2(-dir.y, dir.x);
                    float w = 0.005f;
                    list.Add(b + n * w);
                    list.Insert(0, b - n * w);
                }
                else
                {
                    // 处理线条中间点
                    var p0 = currentSegmentPoints[i - 1];
                    var p1 = currentSegmentPoints[i];
                    var p2 = last ? p1 : currentSegmentPoints[i + 1];
                    
                    var n0 = (p1 - p0).normalized;
                    var n1 = (p2 - p1).normalized;
                    var n = (n0 + n1).normalized;
                    if (n == Vector2.zero) n = new Vector2(-n0.y, n0.x);
                    
                    float w = currentSegmentBrushSizes[i] * 0.005f;
                    var outP = p1 + new Vector2(-n.y, n.x) * w;
                    var inP = p1 - new Vector2(-n.y, n.x) * w;
                    
                    if (prependPrev && i == 1)
                    {
                        // 连接到前一个线段
                        var prev = drawElements[^1].Polygon.points;
                        list.Add(prev[^1]);
                        list.Insert(0, prev[0]);
                    }
                    else
                    {
                        list.Add(outP);
                        list.Insert(0, inP);
                    }
                }
            }
            
            return isFinalizing ? new List<Vector2>(list) : list;
        }

        /// <summary>
        /// 更新当前多边形，用于实时预览
        /// </summary>
        private void UpdateCurrentPolygon()
        {
            if (currentPolygon == null) return;
            if (currentSegmentPoints.Count < 2) return;
            
            polygonPointsBuffer.Clear();
            var pts = polygonPointsBuffer;
            
            pts.Add(currentSegmentPoints[0]);
            for (int i = 1; i < currentSegmentPoints.Count; i++)
            {
                var a = currentSegmentPoints[i - 1];
                var b = currentSegmentPoints[i];
                var c = i == currentSegmentPoints.Count - 1 ? b : currentSegmentPoints[i + 1];
                
                var n0 = (b - a).normalized;
                var n1 = (c - b).normalized;
                var n = (n0 + n1).normalized;
                if (n == Vector2.zero) n = new Vector2(-n0.y, n0.x);
                
                float w = currentSegmentBrushSizes[i] * 0.005f;
                var outP = b + new Vector2(-n.y, n.x) * w;
                var inP = b - new Vector2(-n.y, n.x) * w;
                
                pts.Add(outP);
                pts.Insert(0, inP);
            }
            
            currentPolygon.SetPoints(pts);
        }

        /// <summary>
        /// 处理自相交情况，分割线段
        /// </summary>
        private void HandleSelfIntersection()
        {
            if (currentSegmentPoints.Count < 3) return;
            
            var lastPoint = currentSegmentPoints[^1];
            var lastBrush = currentSegmentBrushSizes[^1];
            
            currentSegmentPoints.RemoveAt(currentSegmentPoints.Count - 1);
            currentSegmentBrushSizes.RemoveAt(currentSegmentBrushSizes.Count - 1);
            
            if (currentSegmentPoints.Count >= 2)
            {
                FinalizeCurrentSegmentInternal();
            }
            
            StartNewSegment();
            
            currentSegmentPoints.Add(lastPoint);
            currentSegmentBrushSizes.Add(lastBrush);
            UpdateCurrentPolygon();
        }

        /// <summary>
        /// 检查多边形是否自相交
        /// </summary>
        private bool CheckSelfIntersection(List<Vector2> points)
        {
            int count = points.Count;
            if (count < 4) return false;
            
            for (int i = 0; i < count; i++)
            {
                Vector2 p1 = points[i];
                Vector2 p2 = points[(i + 1) % count];
                
                for (int j = i + 2; j < count; j++)
                {
                    if (Math.Abs(i - j) == count - 1) continue;
                    
                    Vector2 p3 = points[j];
                    Vector2 p4 = points[(j + 1) % count];
                    
                    // 快速排斥实验
                    if (Math.Max(p1.x, p2.x) < Math.Min(p3.x, p4.x) ||
                        Math.Min(p1.x, p2.x) > Math.Max(p3.x, p4.x) ||
                        Math.Max(p1.y, p2.y) < Math.Min(p3.y, p4.y) ||
                        Math.Min(p1.y, p2.y) > Math.Max(p3.y, p4.y))
                    {
                        continue;
                    }

                    if (LineSegmentsIntersect(p1, p2, p3, p4))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查两条线段是否相交（使用方向法）
        /// </summary>
        private bool LineSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Direction(c, d, a);
            float d2 = Direction(c, d, b);
            float d3 = Direction(a, b, c);
            float d4 = Direction(a, b, d);
            
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        /// <summary>
        /// 计算方向向量，用于线段相交检测
        /// </summary>
        private float Direction(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
        }

        /// <summary>
        /// 创建新的压力线段实例
        /// </summary>
        private DrawableShape CreateNewPressureSegment(Vector2 startPoint, float brushSize, DrawModule dm)
        {
            try
            {
                var shapePrefab = DrawHelpers.Instance.FilledShape;
                var drawableShape = UnityEngine.Object.Instantiate(shapePrefab);
                
                byte bs = (byte)Mathf.Clamp(brushSize, 1f, 99f);
                string owner = "local";
                
                try
                {
                    owner = LocalPlayfabIdProp?.GetValue(dm) as string;
                    if (string.IsNullOrEmpty(owner)) owner = "local";
                }
                catch {}
                
                drawableShape.Init(bs, dm.GetActiveColorToUse(), dm.SortOrder.GetNextSortOrder(false), DrawHelpers.GetSortingLayer(dm.SelectedLayer, false, false), owner, dm);
                
                try
                {
                    var layerProp = AccessTools.Property(typeof(DrawableElement), "Layer");
                    if (layerProp != null)
                    {
                        layerProp.SetValue(drawableShape, dm.SelectedLayer);
                    }
                }
                catch (Exception ex)
                {
                    DrawGuessPluginLoader.Log.LogError($"CreateNewPressureSegment: 设置Layer属性失败: {ex}");
                }
                
                drawableShape.transform.position = Vector3.zero;
                var t = drawableShape.transform;
                t.SetParent(dm.LineParent);
                var lp = t.localPosition;
                t.localPosition = new Vector3(lp.x, lp.y, dm.SortOrder.NextLineZPos(false));
                
                AddToCreatedDrawables(drawableShape);
                drawableShape.SetHoverActive(true); 
                UpdateToolComponents(drawableShape);
                
                return drawableShape;
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"CreateNewPressureSegment错误: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 更新工具组件状态，根据当前激活工具
        /// </summary>
        private void UpdateToolComponents(DrawableShape drawableShape)
        {
            try
            {
                var activeTool = drawModule.ActiveDrawTool;
                
                if (activeTool == DrawModule.DrawTool.Eraser)
                {
                    drawableShape.LineErase.SetLineSelectActive(true);
                }
                else if (activeTool == DrawModule.DrawTool.Fill)
                {
                    drawableShape.LineFillable.SetLineSelectActive(true);
                    drawableShape.SetHoverActive(true);
                }
                else if (activeTool == DrawModule.DrawTool.Dropper)
                {
                    drawableShape.LineDropperable.SetLineSelectActive(true);
                }
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"UpdateToolComponents错误: {ex}");
            }
        }
    }
}
