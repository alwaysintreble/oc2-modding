using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Team17.Online.Multiplayer.Messaging;
using OrderController;

namespace OC2Modding
{
    public static class Nerfs
    {
        static int removedPlates = 0;
        static bool finishedFirstPass = false;
        static List<int> PlayersWearingBackpacks = new List<int>();

        public static void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Nerfs));
        }

        [HarmonyPatch(typeof(LoadingScreenFlow), nameof(LoadingScreenFlow.LoadScene))]
        [HarmonyPrefix]
        private static void LoadScene()
        {
            removedPlates = 0;
            finishedFirstPass = false;
            PlayersWearingBackpacks.Clear();
        }

        [HarmonyPatch(typeof(ServerAttachStation), "OnItemPlaced")]
        [HarmonyPrefix]
        private static bool OnItemPlaced(ref GameObject _objectToPlace)
        {
            OC2Modding.Log.LogInfo($"Placed '{_objectToPlace.name}'");

            if (finishedFirstPass)
            {
                return true; // this is just regular gameplay
            }

            if (OC2Config.DisableCoal && _objectToPlace.name == "utensil_coalbucket_01")
            {
                _objectToPlace.Destroy();
                return false;
            }

            bool isPlate =
                _objectToPlace.name.StartsWith("equipment_plate_01") ||
                _objectToPlace.name.StartsWith("Plate ") ||
                _objectToPlace.name.StartsWith("equipment_mug_01") ||
                _objectToPlace.name.StartsWith("DLC08_equipment_tray") ||
                _objectToPlace.name.StartsWith("DLC11_equipment_glass_01") ||
                _objectToPlace.name.StartsWith("equipment_glass_01")
                ;

            if (isPlate)
            {
                if (OC2Config.PlatesStartDirty)
                {
                    removedPlates++;
                    _objectToPlace.Destroy();
                    return false;
                }
                else if (OC2Config.DisableOnePlate && removedPlates == 0)
                {
                    removedPlates++;
                    _objectToPlace.Destroy();
                    return false;
                }
            }

            if (OC2Config.DisableFireExtinguisher && (_objectToPlace.name.StartsWith("utensil_fire_extinguisher_") || _objectToPlace.name.StartsWith("DLC08_utensil_fire_extinguisher")))
            {
                _objectToPlace.Destroy();
                return false;
            }

            if (OC2Config.DisableBellows && _objectToPlace.name == "utensil_bellows_01")
            {
                _objectToPlace.Destroy();
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(ServerPlateReturnStation), nameof(ServerPlateReturnStation.UpdateSynchronising))]
        [HarmonyPrefix]
        private static void UpdateSynchronising(ref PlateReturnStation ___m_returnStation)
        {
            if (___m_returnStation.m_startingPlateNumber != 0 || removedPlates == 0 || finishedFirstPass)
            {
                return; // We already did this patch
            }

            if (___m_returnStation.name.Contains("rying"))
            {
                return; // This is the drying station for a sink
            }

            ___m_returnStation.m_startingPlateNumber = removedPlates;

            if (OC2Config.DisableOnePlate)
            {
                ___m_returnStation.m_startingPlateNumber -= 1;
            }

            finishedFirstPass = true;

            OC2Modding.Log.LogInfo($"Added {___m_returnStation.m_startingPlateNumber} plates to the serving window");
        }

        [HarmonyPatch(typeof(ServerKitchenFlowControllerBase), "OnSuccessfulDelivery")]
        [HarmonyPostfix]
        private static void OnSuccessfulDelivery(ref ServerKitchenFlowControllerBase __instance)
        {
            var monitor = __instance.GetMonitorForTeam(0);
            if (monitor.Score.TotalMultiplier > OC2Config.MaxTipCombo)
            {
                monitor.Score.TotalMultiplier = OC2Config.MaxTipCombo;
            }
        }

        /* Edit the message sent to clients to also show the new limit */
        [HarmonyPatch(typeof(KitchenFlowMessage), nameof(KitchenFlowMessage.SetScoreData))]
        [HarmonyPostfix]
        private static void SetScoreData(ref TeamMonitor.TeamScoreStats ___m_teamScore)
        {
            if (___m_teamScore.TotalMultiplier > OC2Config.MaxTipCombo)
            {
                ___m_teamScore.TotalMultiplier = OC2Config.MaxTipCombo;
            }
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), nameof(ClientPlayerControlsImpl_Default.ApplyServerEvent))]
        [HarmonyPrefix]
        private static bool ApplyServerEvent(ref Serialisable serialisable)
        {
            InputEventMessage inputEventMessage = (InputEventMessage)serialisable;
            InputEventMessage.InputEventType inputEventType = inputEventMessage.inputEventType;

            switch (inputEventType)
            {
                case InputEventMessage.InputEventType.Dash:
                case InputEventMessage.InputEventType.DashCollision:
                    {
                        return !OC2Config.DisableDash;
                    }
                case InputEventMessage.InputEventType.Catch:
                    {
                        return !OC2Config.DisableCatch;
                    }
                case InputEventMessage.InputEventType.EndThrow:
                    {
                        return !OC2Config.DisableThrow;
                    }
                default:
                    {
                        break;
                    }
            }

            return true;
        }

        private static bool InReceiveThrowEvent = false;

        [HarmonyPatch(typeof(ServerPlayerControlsImpl_Default), nameof(ServerPlayerControlsImpl_Default.ReceiveThrowEvent))]
        [HarmonyPrefix]
        private static void ReceiveThrowEventPrefix()
        {
            InReceiveThrowEvent = true;
        }

        [HarmonyPatch(typeof(ServerPlayerControlsImpl_Default), nameof(ServerPlayerControlsImpl_Default.ReceiveThrowEvent))]
        [HarmonyPostfix]
        private static void ReceiveThrowEventPostfix()
        {
            InReceiveThrowEvent = false;
        }

        [HarmonyPatch(typeof(ServerThrowableItem), nameof(ServerThrowableItem.CanHandleThrow))]
        [HarmonyPostfix]
        private static void CanHandleThrow(ref bool __result)
        {
            if (OC2Config.DisableThrow && __result)
            {
                if (InReceiveThrowEvent)
                {
                    // Only play sfx when in the handler for received throw events
                    // this is needed because for some reason teleporters are the only other
                    // scripting in the game which check this function
                    OC2Helpers.PlayErrorSfx();
                }
                
                __result = false;
            }
        }

        [HarmonyPatch(typeof(ServerPilotMovement), "Update_Movement")]
        [HarmonyPrefix]
        private static bool Update_Movement(ref ServerThrowableItem __instance)
        {
            if (__instance.gameObject.name == "DLC10_Pushable_Object" && OC2Config.DisableWokDrag)
            {
                return false;
            }

            if (__instance.gameObject.name == "MovingSection" && OC2Config.DisableControlStick)
            {
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(PlayerControls), nameof(PlayerControls.ScanForCatch))]
        [HarmonyPostfix]
        private static void ScanForCatch(ref ICatchable __result, ref PlayerControls __instance)
        {
            if (__result != null && OC2Config.DisableCatch)
            {
                __result = null;
            }
        }

        [HarmonyPatch(typeof(ServerPlayerControlsImpl_Default), nameof(ServerPlayerControlsImpl_Default.StartDash))]
        [HarmonyPrefix]
        private static bool StartDash()
        {
            return !OC2Config.DisableDash;
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "Update_Movement")]
        [HarmonyPrefix]
        private static void Update_Movement(ref float ___m_dashTimer)
        {
            if (___m_dashTimer > 0f && OC2Config.DisableDash)
            {
                ___m_dashTimer = 0f;
                OC2Helpers.PlayErrorSfx();
            }
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), nameof(ClientPlayerControlsImpl_Default.Init))]
        [HarmonyPostfix]
        private static void Init(ref PlayerControls ___m_controls)
        {
            if (!OC2Config.WeakDash)
            {
                return;
            }

            var m_movement_prop = ___m_controls.GetType().GetField("m_movement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            PlayerControls.MovementData m_movement = ((PlayerControls.MovementData)m_movement_prop.GetValue(___m_controls));
            
            float dashSpeedScale = 0.75f;
            float dashCooldownScale = 1.5f;

            m_movement.DashSpeed *= dashSpeedScale;
            m_movement.DashCooldown *= dashCooldownScale;

            m_movement_prop.SetValue(___m_controls, m_movement);
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "DoDash")]
        [HarmonyPrefix]
        private static bool DoDash()
        {
            return !OC2Config.DisableDash;
        }

        [HarmonyPatch(typeof(ServerMapAvatarControls), "Update_Movement")]
        [HarmonyPrefix]
        private static void Update_Movement__Prefix(ref ILogicalButton ___m_dashButton, ref ILogicalButton __state)
        {
            __state = ___m_dashButton;
            if (OC2Config.DisableDash)
            {
                ___m_dashButton = null;
            }
        }

        [HarmonyPatch(typeof(ServerMapAvatarControls), "Update_Movement")]
        [HarmonyPostfix]
        private static void Update_Movement_Postfix(ref ILogicalButton ___m_dashButton, ref ILogicalButton __state)
        {
            ___m_dashButton = __state; // Restore, just in case
        }


        [HarmonyPatch(typeof(ServerWashingStation), nameof(ServerWashable.UpdateSynchronising))]
        [HarmonyPrefix]
        private static void UpdateSynchronising(ref WashingStation ___m_washingStation)
        {
            ___m_washingStation.m_cleanPlateTime = 2.0f * OC2Config.WashTimeMultiplier;
        }

        [HarmonyPatch(typeof(ServerCookingHandler), nameof(ServerCookingHandler.Cook))]
        [HarmonyPrefix]
        private static void Cook(ref float _cookingDeltatTime, ref bool __result, ref CookingStateMessage ___m_ServerData, ref ServerCookingHandler __instance)
        {
            if (___m_ServerData.m_cookingState != CookingUIController.State.Idle && ___m_ServerData.m_cookingState != CookingUIController.State.Progressing)
            {
                _cookingDeltatTime *= OC2Config.BurnSpeedMultiplier;
            }
        }

        [HarmonyPatch(typeof(ServerOrderControllerBase), "IsFull")]
        [HarmonyPostfix]
        private static void IsFull(ref bool __result, ref List<ServerOrderData> ___m_activeOrders, ref int ___m_maxOrdersAllowed)
        {
            __result = ___m_activeOrders.Count >= ___m_maxOrdersAllowed + OC2Config.MaxOrdersOnScreenOffset;
        }

        [HarmonyPatch(typeof(ServerWorkableItem), nameof(ServerWorkableItem.DoWork))]
        [HarmonyPatch(new Type[] { typeof(ServerAttachStation), typeof(GameObject) })]
        [HarmonyPrefix]
        private static void DoWork(ref WorkableItem ___m_workable)
        {
            ___m_workable.m_stages = Math.Max((int)(8.0f * OC2Config.ChoppingTimeScale), 2);
        }

        [HarmonyPatch(typeof(ServerEmoteWheel), "StartEmote")]
        [HarmonyPrefix]
        private static bool StartEmote(ref ServerEmoteWheel __instance, ref EmoteWheelMessage _message)
        {
            if (OC2Config.LockedEmotes.Contains(_message.m_emoteIdx))
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ClientEmoteWheel), nameof(ClientEmoteWheel.StartEmote))]
        [HarmonyPrefix]
        private static bool StartEmoteClient(ref ClientEmoteWheel __instance, ref int _emoteIdx)
        {
            if (OC2Config.LockedEmotes.Contains(_emoteIdx))
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ClientEmoteWheel), nameof(ClientEmoteWheel.RequestEmoteStart))]
        [HarmonyPrefix]
        private static bool RequestEmoteStart(ref int _emoteIdx)
        {
            if (OC2Config.LockedEmotes.Contains(_emoteIdx))
            {
                OC2Helpers.PlayErrorSfx();
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ServerBackpack), nameof(ServerBackpack.HandlePickup))]
        [HarmonyPostfix]
        private static void CanHandlePickup(ref ICarrier _carrier)
        {
            if (OC2Config.BackpackMovementScale == 1.0f)
            {
                return;
            }

            string name = _carrier.AccessGameObject().name;
            try
            {
                int playerNum = Int32.Parse($"{name[name.Length - 1]}");
                PlayersWearingBackpacks.Add(playerNum);
                OC2Modding.Log.LogInfo($"Player #{playerNum} picked up a backpack");
            }
            catch { }
        }

        [HarmonyPatch(typeof(ClientPlayerControlsImpl_Default), "Update_Movement")]
        [HarmonyPrefix]
        private static void Update_Movement(ref PlayerControls ___m_controls, ref PlayerIDProvider ___m_controlsPlayer)
        {
            if (___m_controls.MovementScale != 1.0f && ___m_controls.MovementScale != 0.0f)
            {
                return;
            }

            PlayerInputLookup.Player id = ___m_controlsPlayer.GetID();
            int playerNum = ((int)id) + 1;

            if ((OC2Helpers.GetCurrentPlayerCount() == 1 && PlayersWearingBackpacks.Count > 0) || PlayersWearingBackpacks.Contains(playerNum))
            {
                OC2Modding.Log.LogInfo($"Player #{playerNum}'s speed set to {OC2Config.BackpackMovementScale}");
                ___m_controls.SetMovementScale(OC2Config.BackpackMovementScale);
            }
        }

        // [HarmonyPatch(typeof(ServerPlayerRespawnManager), "KillOrRespawn")]
        // [HarmonyPrefix]
        // private static void KillOrRespawn(ref GameObject _gameObject)
        // {
        //     OC2Modding.Log.LogInfo($"Respawning {_gameObject.gameObject.name}...");
        // }

        [HarmonyPatch(typeof(ServerPlayerRespawnBehaviour), nameof(ServerPlayerRespawnBehaviour.StartSynchronising))]
        [HarmonyPostfix]
        private static void StartSynchronising(ref PlayerRespawnBehaviour ___m_PlayerRespawnBehaviour)
        {
            ___m_PlayerRespawnBehaviour.m_respawnTime = OC2Config.RespawnTime;
        }

        [HarmonyPatch(typeof(ClientPlayerRespawnBehaviour), nameof(ClientPlayerRespawnBehaviour.StartSynchronising))]
        [HarmonyPostfix]
        private static void StartSynchronisingClient(ref PlayerRespawnBehaviour ___m_PlayerRespawnBehaviour)
        {
            ___m_PlayerRespawnBehaviour.m_respawnTime = OC2Config.RespawnTime;
        }

        private static double previousDrinkTime = 0;
        [HarmonyPatch(typeof(ServerInteractable), nameof(ServerInteractable.CanInteract))]
        [HarmonyPostfix]
        private static void CanInteract(ref ServerInteractable __instance, ref bool __result)
        {
            if (OC2Config.CarnivalDispenserRefactoryTime <= 0.001f)
            {
                return; // This patch would have no effect
            }

            if (!__result)
            {
                return; // It's already not interactable
            }

            // OC2Modding.Log.LogInfo($"Can Interact With '{__instance.gameObject.name}'?");

            if (
                __instance.gameObject.name != "p_dlc08_button_Drinks" &&
                __instance.gameObject.name != "p_dlc08_button_Condiments" &&
                __instance.gameObject.name != "Switch" // TODO: this is for SoBo drinks, it probably conflicts with a lot of other things
            )
            {
                return; // It's not an interactable we care about
            }

            float checkTime = Time.time;
            if (checkTime > previousDrinkTime && checkTime - previousDrinkTime > OC2Config.CarnivalDispenserRefactoryTime)
            {
                // It has been beyond the cooldown time
                previousDrinkTime = checkTime;
                return;
            }

            // Reject the button push
            OC2Helpers.PlayErrorSfx();
            __result = false;
        }

        [HarmonyPatch(typeof(WorldMapSwitch), nameof(WorldMapSwitch.CanBePressed))]
        [HarmonyPostfix]
        private static void CanBePressed(ref bool __result)
        {
            if (OC2Config.DisableRampButton && __result)
            {
                // Reject the button push
                OC2Helpers.PlayErrorSfx();
                __result = false;
            }
        }

        // [HarmonyPatch(typeof(ServerDirtyPlateStack), nameof(ServerDirtyPlateStack.OnSurfaceDeplacement))]
        // [HarmonyPostfix]
        // private static void OnSurfaceDeplacement(ref ServerDirtyPlateStack __instance, ref ServerStack ___m_stack, ref ServerAttachStation ___m_attachStation)
        // {
        //     OC2Modding.Log.LogInfo("OnSurfaceDeplacement");
        //     // ___m_attachStation.AddItem(, Vector2.up);

        //     GameObject gameObject = ___m_stack.RemoveFromStack();
        //     ServerHandlePickupReferral serverHandlePickupReferral = gameObject.RequestComponent<ServerHandlePickupReferral>();
        //     if (serverHandlePickupReferral != null && serverHandlePickupReferral.GetHandlePickupReferree() == __instance)
        //     {
        //         serverHandlePickupReferral.SetHandlePickupReferree(null);
        //     }
        //     ServerHandlePlacementReferral serverHandlePlacementReferral = gameObject.RequestComponent<ServerHandlePlacementReferral>();
        //     if (serverHandlePlacementReferral != null && serverHandlePlacementReferral.GetHandlePlacementReferree() == __instance)
        //     {
        //         serverHandlePlacementReferral.SetHandlePlacementReferree(null);
        //     }
            
        //     ___m_attachStation.AddItem(gameObject, Vector2.up);
        // }
    }
}
