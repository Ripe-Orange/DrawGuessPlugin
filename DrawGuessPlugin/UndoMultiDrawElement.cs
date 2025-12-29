using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System;

namespace DrawGuessPlugin
{
    public class UndoMultiDrawElement : IUndo
    {
        private readonly List<DrawableElement> elements;
        private readonly DrawModule drawModule;

        private static PropertyInfo _localPlayfabIdProp;
        private static PropertyInfo LocalPlayfabIdProp => _localPlayfabIdProp ??= AccessTools.Property(typeof(DrawModule), "LocalPlayfabId");

        private static MethodInfo _onSendDeletePreciseMethod;
        private static MethodInfo OnSendDeletePreciseMethod => _onSendDeletePreciseMethod ??= AccessTools.Method(typeof(DrawModule), "OnSendDeletePrecise", new Type[] { typeof(int) });

        public UndoMultiDrawElement(IEnumerable<DrawableElement> elements, DrawModule drawModule)
        {
            this.elements = new List<DrawableElement>(elements);
            this.drawModule = drawModule;
        }

        public void Undo()
        {
            if (drawModule == null && elements.Count > 0) return;

            string localId = null;
            try
            {
                localId = LocalPlayfabIdProp?.GetValue(drawModule) as string;
            }
            catch {}

            foreach (DrawableElement element in this.elements)
            {
                if (element != null)
                {
                    element.MarkAsDeleted(true);
                    
                    bool isMine = element.Owner == localId;
                    if (isMine)
                    {
                        try
                        {
                            OnSendDeletePreciseMethod?.Invoke(drawModule, new object[] { element.Ident });
                        }
                        catch (Exception ex)
                        {
                            DrawGuessPluginLoader.Log.LogError($"UndoMultiDrawElement: Failed to sync delete: {ex}");
                        }
                    }
                }
            }
        }

        public void Redo()
        {
             foreach (DrawableElement element in this.elements)
             {
                 if (element != null)
                 {
                     element.MarkAsDeleted(false);
                    
                     bool isMine = false;
                     try { isMine = element.Owner == (LocalPlayfabIdProp?.GetValue(drawModule) as string); } catch {}

                     if (isMine)
                     {
                         // 同步 Redo logic
                         try
                         {
                             string moduleName = drawModule.GetType().Name;
                            
                             // 竞猜模式 (MLDrawModule / SyncDrawModule)
                             if (moduleName == "MLDrawModule" || moduleName == "SyncDrawModule")
                             {
                                 var playerField = AccessTools.Field(drawModule.GetType(), "player");
                                 if (playerField != null) 
                                 {
                                     var player = playerField.GetValue(drawModule);
                                     if (player != null)
                                     {
                                         string playerTypeName = player.GetType().Name;
                                        
                                         if (playerTypeName == "SyncDGPlayer")
                                         {
                                             var addLineMethod = AccessTools.Method(player.GetType(), "DrawAddLine", new Type[] { typeof(LineInformation) });
                                             if (addLineMethod != null)
                                             {
                                                 var lineInfo = element.ToLineInformation();
                                                 addLineMethod.Invoke(player, new object[] { lineInfo });
                                             }
                                         }
                                         else 
                                         {
                                             var distributeMethod = AccessTools.Method(player.GetType(), "DistributeDrawingPartial", new Type[] { typeof(DrawableElement) });
                                             if (distributeMethod != null)
                                             {
                                                 distributeMethod.Invoke(player, new object[] { element });
                                             }
                                         }
                                     }
                                 }
                             }
                             // 接龙模式 (ChainDrawModule)
                             else if (moduleName == "ChainDrawModule")
                             {
                                 var chainPlayerType = AccessTools.TypeByName("ChainDGPlayer");
                                 if (chainPlayerType != null)
                                 {
                                     var localPlayerProp = AccessTools.Property(chainPlayerType, "ChainLocalPlayer");
                                     if (localPlayerProp != null)
                                     {
                                         var localPlayer = localPlayerProp.GetValue(null);
                                         if (localPlayer != null)
                                         {
                                             var distributeMethod = AccessTools.Method(chainPlayerType, "DistributeDrawingPartial", new Type[] { typeof(DrawableElement) });
                                             if (distributeMethod != null)
                                             {
                                                 distributeMethod.Invoke(localPlayer, new object[] { element });
                                             }
                                         }
                                     }
                                 }
                             }
                             // 茶绘模式
                             else if (LobbyManager.Instance != null && LobbyManager.Instance.SyncController != null)
                             {
                                 var lineInfo = element.ToLineInformation();
                                 LobbyManager.Instance.SyncController.LobbyAddDrawLine(lineInfo);
                             }
                         }
                         catch (Exception ex)
                         {
                             DrawGuessPluginLoader.Log.LogError($"UndoMultiDrawElement: Failed to sync redo: {ex}");
                         }
                     }
                 }
             }
        }
    }
}