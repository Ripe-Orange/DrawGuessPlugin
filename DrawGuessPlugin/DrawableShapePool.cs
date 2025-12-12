using System.Collections.Generic;
using UnityEngine;

namespace DrawGuessPlugin
{
    public static class DrawableShapePool
    {
        // 简单对象池，复用 DrawableShape 以减少实例化开销
        static readonly Stack<DrawableShape> pool = new Stack<DrawableShape>(32);

        public static DrawableShape Get(DrawableShape prefab, DrawModule dm, byte brushSize, Color color, int sortOrder, int sortingLayer, string owner)
        {
            DrawableShape shape;
            if (pool.Count > 0)
            {
                shape = pool.Pop();
                shape.gameObject.SetActive(true);
            }
            else
            {
                shape = Object.Instantiate(prefab);
            }
            shape.Init(brushSize, color, sortOrder, sortingLayer, owner, dm);
            shape.transform.position = Vector3.zero;
            var t = shape.transform;
            t.SetParent(dm.LineParent);
            var lp = t.localPosition;
            t.localPosition = new Vector3(lp.x, lp.y, dm.SortOrder.NextLineZPos(false));
            return shape;
        }

        public static void Return(DrawableShape shape)
        {
            if (shape == null) return;
            shape.gameObject.SetActive(false);
            pool.Push(shape);
        }
    }
}