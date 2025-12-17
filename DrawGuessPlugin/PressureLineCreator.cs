using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Shapes;

namespace DrawGuessPlugin
{
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
        
        private static FieldInfo _createdDrawablesField;
        private static FieldInfo CreatedDrawablesField => _createdDrawablesField ??= AccessTools.Field(typeof(DrawModule), "CreatedDrawablesByMe");
        
        private static FieldInfo _existingLinesField;
        private static PropertyInfo _localPlayfabIdProp; 
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        private static FieldInfo _mlPlayerField;
        private static bool _checkedForMlModule = false;
        private static bool _isMlModule = false;

        public bool Finished { get; private set; }
        public int LineGroupID => lineGroupID;

        public void SetBrushSizeRange(float min, float max)
        {
            minBrushSize = min;
            maxBrushSize = max;
        }

        public void SetDrawModule(DrawModule dm)
        {
            drawModule = dm;
            pressureSmoother = new SmoothedValue(5);
            CheckModuleType();
            
            if (_isMlModule)
            {
                DrawGuessPluginLoader.Log.LogInfo("PressureLineCreator: MLDrawModule detected, disabling PressureLineCreator to use native drawing.");
                this.enabled = false;
                return;
            }
        }

        private void CheckModuleType()
        {
            if (drawModule == null) return;
            
            if (!_checkedForMlModule)
            {
                var type = drawModule.GetType();
                string typeName = type.Name;
                
                if (typeName.Contains("Sync") || typeName == "MLDrawModule" || typeName.Contains("Stage") || typeName.Contains("ML"))
                {
                    _isMlModule = true;
                    DrawGuessPluginLoader.Log.LogInfo($"PressureLineCreator: Detected Stage/Sync Module by name ({typeName}).");
                }
                else 
                {
                    try
                    {
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        _mlPlayerField = type.GetField("player", flags);
                        if (_mlPlayerField != null)
                        {
                            var timerField = type.GetField("timer", flags);
                            if (timerField != null)
                            {
                                _isMlModule = true;
                                DrawGuessPluginLoader.Log.LogInfo("PressureLineCreator: Detected Stage/MLDrawModule capabilities (Field Check).");
                            }
                        }
                    }
                    catch { }
                }
                _checkedForMlModule = true;
            }
        }

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
                DrawGuessPluginLoader.Log.LogError($"UpdateEraseButtons Error: {ex}");
            }
        }

        private void AddToCreatedDrawables(DrawableElement elem)
        {
            try
            {
                var collection = CreatedDrawablesField?.GetValue(drawModule) as System.Collections.ObjectModel.ObservableCollection<DrawableElement>;
                if (collection != null)
                {
                    if (!collection.Contains(elem))
                    {
                        collection.Add(elem);
                    }
                }
                else
                {
                    DrawGuessPluginLoader.Log.LogError("AddToCreatedDrawables: Collection is null");
                }


                AddToLobbyExistingLines(elem);
            }
            catch (Exception ex)
            {
                DrawGuessPluginLoader.Log.LogError($"AddToCreatedDrawables Error: {ex}");
            }
        }

        private void AddToLobbyExistingLines(DrawableElement elem)
        {
            try
            {

                if (_existingLinesField == null)
                {

                    _existingLinesField = drawModule.GetType().GetField("ExistingLinesInMulti",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
                            var dictType = typeof(Dictionary<int, DrawableElement>);
                            var newDict = Activator.CreateInstance(dictType);
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
                
            }
        }

        public void StartNewLine(Vector2 startPoint, DrawableShape drawableShape)
        {
            if (_isMlModule) return; // 双重检查

            lineGroupID = ++lastLineGroupID;
            fullLineSegments.Clear();
            fullLineSegments.Add(drawableShape);
            
            PressureLine.RegisterSegment(lineGroupID, drawableShape);
            
            AddToCreatedDrawables(drawableShape);
            UpdateEraseButtons();
            
            drawableShape.SetHoverActive(true);
            
            currentDrawElement = drawableShape;
            currentPolygon = currentDrawElement.Polygon;

            drawElements.Clear();
            mainPoints.Clear();
            brushSizes.Clear();
            currentSegmentPoints.Clear();
            currentSegmentBrushSizes.Clear();

            Finished = false;
            enabled = true;

            mainPoints.Add(startPoint);
            float initialPressure = Pen.current != null ? Pen.current.pressure.ReadValue() : 1f;
            brushSizes.Add(Mathf.Lerp(1f, 99f, initialPressure));
            currentSegmentPoints.Add(startPoint);
            currentSegmentBrushSizes.Add(brushSizes[0]);
        }

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
            currentPolygon = currentDrawElement.Polygon;

            drawElements.Clear();
            mainPoints.Clear();
            brushSizes.Clear();
            currentSegmentPoints.Clear();
            currentSegmentBrushSizes.Clear();

            Finished = false;
            enabled = true;
        }

        public void AppendPoint(Vector2 worldPoint, float rawPressure)
        {
            if (drawModule == null || Finished) return;
            float clamped = Mathf.Clamp(rawPressure, 0.001f, 1f);
            pressureSmoother.AddValue((int)(clamped * 1000f));
            float smoothed = pressureSmoother.SmoothValue(5) / 1000f;
            float t = smoothed < 0.15f ? 0f : (smoothed - 0.15f) / 0.85f;
            float brushSize = Mathf.Lerp(minBrushSize, maxBrushSize, t);
            float minPointDistance = GetDynamicMinPointDistance(brushSize);
            int dynamicSegmentSize = GetDynamicSegmentSize(brushSize);

            if (mainPoints.Count == 0)
            {
                mainPoints.Add(worldPoint);
                brushSizes.Add(brushSize);
                currentSegmentPoints.Add(worldPoint);
                currentSegmentBrushSizes.Add(brushSize);
                return;
            }

            if (Vector2.Distance(worldPoint, mainPoints[mainPoints.Count - 1]) < minPointDistance) return;

            mainPoints.Add(worldPoint);
            brushSizes.Add(brushSize);
            currentSegmentPoints.Add(worldPoint);
            currentSegmentBrushSizes.Add(brushSize);

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

            var list = new List<DrawableElement>();
            foreach (var seg in fullLineSegments)
                if (seg != null) list.Add(seg);

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

        private float GetDynamicMinPointDistance(float brushSize)
        {
            if (brushSize <= 6f) return minPointDistanceMin;
            float t = (brushSize - 6f) / (maxBrushSize - 6f);
            return Mathf.Lerp(minPointDistanceMin, minPointDistanceMax, t);
        }

        private int GetDynamicSegmentSize(float brushSize)
        {
            if (brushSize <= 2f) return 4;
            float t = (brushSize - 2f) / (maxBrushSize - 2f);
            return Mathf.RoundToInt(Mathf.Lerp(segmentSizeMin, segmentSizeMax, t));
        }

        private void StartNewSegment()
        {
            var lastPoint = currentSegmentPoints[currentSegmentPoints.Count - 1];
            var lastBrush = currentSegmentBrushSizes[currentSegmentBrushSizes.Count - 1];
            currentDrawElement = CreateNewPressureSegment(lastPoint, lastBrush, drawModule);
            
            if (currentDrawElement == null)
            {
                DrawGuessPluginLoader.Log.LogError("StartNewSegment: Failed to create new segment. Aborting line.");
                Finished = true;
                return;
            }

            currentPolygon = currentDrawElement.Polygon;
            currentSegmentPoints.Clear();
            currentSegmentBrushSizes.Clear();
            currentSegmentPoints.Add(lastPoint);
            currentSegmentBrushSizes.Add(lastBrush);
        }

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

        private void FinalizeCurrentSegmentInternal()
        {
            currentPolygon.SetPoints(GeneratePolygonPoints(true));
            var colliderPoints = currentPolygon.points.ToArray();
            currentDrawElement.SetColliderPoints(colliderPoints);
            drawElements.Add(currentDrawElement);
            drawModule.CheckSpecialMode(currentDrawElement, true);
            
            drawModule.SortOrder.IncreaseStrokeCount(currentDrawElement.DrawType == DrawElementT.Fill);
            
            PressureLine.RegisterSegment(lineGroupID, currentDrawElement);
            fullLineSegments.Add(currentDrawElement);
            
            SyncCurrentSegment();
        }

        private void SyncCurrentSegment()
        {

            if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
            {
                try
                {
                    var lineInfo = currentDrawElement.ToLineInformation();
                    LobbyManager.Instance.SyncController.LobbyAddDrawLine(lineInfo);
                }
                catch (Exception ex)
                {
                    DrawGuessPluginLoader.Log.LogError($"SyncCurrentSegment Lobby Error: {ex}");
                }
            }
        }

        private readonly List<Vector2> polygonPointsBuffer = new List<Vector2>(1024);

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
                    var a = currentSegmentPoints[i - 1];
                    var b = currentSegmentPoints[i];
                    var dir = (b - a).normalized;
                    var n = new Vector2(-dir.y, dir.x);
                    float w = 0.005f;
                    var p1 = b + n * w;
                    var p2 = b - n * w;
                    list.Add(p1);
                    list.Insert(0, p2);
                }
                else
                {
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
                        var prev = drawElements[drawElements.Count - 1].Polygon.points;
                        list.Add(prev[prev.Count - 1]);
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

        private void UpdateCurrentPolygon()
        {
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

        private void HandleSelfIntersection()
        {
            if (currentSegmentPoints.Count < 3) return;

            var lastPoint = currentSegmentPoints[currentSegmentPoints.Count - 1];
            var lastBrush = currentSegmentBrushSizes[currentSegmentBrushSizes.Count - 1];
            
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

        private bool LineSegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Direction(c, d, a);
            float d2 = Direction(c, d, b);
            float d3 = Direction(a, b, c);
            float d4 = Direction(a, b, d);
            if (d1 * d2 < 0 && d3 * d4 < 0) return true;
            return false;
        }

        private float Direction(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
        }

        private DrawableShape CreateNewPressureSegment(Vector2 startPoint, float brushSize, DrawModule dm)
        {
            try
            {
                var shapePrefab = DrawHelpers.Instance.FilledShape;
                var drawableShape = UnityEngine.Object.Instantiate(shapePrefab);
                byte bs = (byte)Mathf.Clamp(brushSize, 1f, 99f);
                string owner = null;
                try
                {
                    owner = LocalPlayfabIdProp?.GetValue(dm) as string;
                }
                catch { }
                if (string.IsNullOrEmpty(owner))
                {
                    owner = "local";
                }
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
                    DrawGuessPluginLoader.Log.LogError($"CreateNewPressureSegment: Failed to set Layer property: {ex}");
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
                DrawGuessPluginLoader.Log.LogError($"CreateNewPressureSegment Error: {ex}");
                return null;
            }
        }

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
                DrawGuessPluginLoader.Log.LogError($"UpdateToolComponents Error: {ex}");
            }
        }
    }
}
