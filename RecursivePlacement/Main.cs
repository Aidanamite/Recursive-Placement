using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;
using System;
using Steamworks;
using System.Globalization;
using Object = UnityEngine.Object;

namespace RecursivePlacement
{
    public class Main : Mod
    {
        Harmony harmony;
        public static string buildKey = "";
        public static string lockKey = "";
        public static float offset = 0;
        public void Start()
        {
            Placement.ghostBlocks = new List<Block>();
            harmony = new Harmony("com.aidanamite.RecursivePlacement");
            harmony.PatchAll();
            if (RAPI.GetLocalPlayer() != null && Traverse.Create(RAPI.GetLocalPlayer().BlockCreator).Field("selectedBuildableItem").GetValue<Item_Base>() != null)
                RAPI.GetLocalPlayer().BlockCreator.SetBlockTypeToBuild(Traverse.Create(RAPI.GetLocalPlayer().BlockCreator).Field("selectedBuildableItem").GetValue<Item_Base>().UniqueName);
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            foreach (Block block in Placement.ghostBlocks)
                Destroy(block.gameObject);
            if (Placement.cost != null)
                Destroy(Placement.cost.gameObject);
            harmony.UnpatchAll(harmony.Id);
            Log("Mod has been unloaded!");
        }



        public void ExtraSettingsAPI_Load()
        {
            buildKey = ExtraSettingsAPI_GetKeybindName("place");
            lockKey = ExtraSettingsAPI_GetKeybindName("lock");
            ExtraSettingsAPI_SettingsClose();
        }
        public void ExtraSettingsAPI_SettingsClose() => offset = Parse(ExtraSettingsAPI_GetInputValue("offset"));
        public static bool ExtraSettingsAPI_Loaded = false;
        public string ExtraSettingsAPI_GetKeybindName(string SettingName) => "";
        public string ExtraSettingsAPI_GetInputValue(string SettingName) => "";


        static float Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;
            if (value.Contains(",") && !value.Contains("."))
                value = value.Replace(',', '.');
            var c = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfoByIetfLanguageTag("en-NZ");
            Exception e = null;
            float r = 0;
            try
            {
                r = float.Parse(value);
            }
            catch (Exception e2)
            {
                e = e2;
            }
            CultureInfo.CurrentCulture = c;
            if (e != null)
                throw e;
            return r;
        }
    }

    public static class ExtentionMethods
    {
        public static void PlaceBlock(this BlockCreator self, Item_Base blockItem, Vector3 position, Vector3 rotation, DPS dps)
        {
            if (Raft_Network.IsHost)
            {
                Message_BlockCreator_PlaceBlock message_BlockCreator_PlaceBlock = new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, self, blockItem.UniqueIndex, SaveAndLoad.GetUniqueObjectIndex(), SaveAndLoad.GetUniqueObjectIndex(), NetworkUpdateManager.GetUniqueBehaviourIndex(), position, rotation, -1, dps);
                ComponentManager<Raft_Network>.Value.RPC(message_BlockCreator_PlaceBlock, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                self.CreateBlock(blockItem, message_BlockCreator_PlaceBlock.LocalPosition, message_BlockCreator_PlaceBlock.LocalEuler, message_BlockCreator_PlaceBlock.dpsType, -1, false, message_BlockCreator_PlaceBlock.blockObjectIndex, message_BlockCreator_PlaceBlock.networkedObjectIndex, message_BlockCreator_PlaceBlock.networkedBehaviourIndex);
                return;
            }
            Message_BlockCreator_PlaceBlock message = new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, self, blockItem.UniqueIndex, 0U, 0U, 0U, position, rotation, -1, dps);
            RAPI.GetLocalPlayer().SendP2P(message, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public static BuildError GetBuildError(this BlockCreator self, Block block) => Traverse.Create(self).Method("CanBuildBlock", new object[] { block }).GetValue<BuildError>();

        public static bool CanBuildBlock(this BlockCreator self, Block block) => self.GetBuildError(block) == BuildError.None;

        public static float Closest(this Block block1, Vector3 direction)
        {
            direction = direction.normalized * 10;
            float minDistance = float.MaxValue;
            Dictionary<Collider, int> Layers = new Dictionary<Collider, int>();
            foreach (var a in block1.onoffColliders)
                if ((LayerMasks.MASK_BlockCreatorOverlap.value & (1 << a.gameObject.layer)) != 0)
                {
                    Layers.Add(a, a.gameObject.layer);
                    a.gameObject.layer = 30;
                }
            foreach (var a in block1.occupyingComponent.allAdvBoxColliders)
            {
                Vector3 center = a.bounds.center;
                RaycastHit hit;
                if (Physics.BoxCast(center - direction / 2, a.size / 2, direction, out hit, a.transform.rotation, direction.magnitude, 1 << 30))
                    if (hit.distance < minDistance)
                        minDistance = hit.distance;
            }
            foreach (var a in block1.onoffColliders)
                a.gameObject.layer = Layers[a];
            return direction.magnitude / 2 - minDistance + Main.offset;
        }
    }

    [HarmonyPatch]
    public class Placement
    {
        public static List<Block> ghostBlocks;
        static Material materialGreen;
        static Material materialRed;
        static Transform lockedPivot;
        public static CostCollection cost;
        static CostCollection buildCost;
        static int placing;
        static bool dontClear = false;

        [HarmonyPatch(typeof(BlockCreator), "SetBlockTypeToBuild", new Type[] { typeof(Item_Base) })]
        [HarmonyPostfix]
        static void BlockCreator_SetBlockTypeToBuild(BlockCreator __instance, GameManager ___gameManager, Transform ___lockedBuildPivot, Item_Base ___selectedBuildableItem)
        {
            materialGreen = ___gameManager.ghostMaterialGreen;
            materialRed = ___gameManager.ghostMaterialRed;
            lockedPivot = ___lockedBuildPivot;
            if (!dontClear)
            {
                foreach (Block block in ghostBlocks)
                    Object.Destroy(block?.gameObject);
                ghostBlocks.Clear();
            }
            if (buildCost == null)
                buildCost = Traverse.Create(ComponentManager<BuildMenu>.Value).Field("costColletionCursor").GetValue<CostCollection>();
            if (cost == null)
            {
                var obj = buildCost.gameObject;
                cost = Object.Instantiate(obj, ComponentManager<CanvasHelper>.Value.transform).GetComponent<CostCollection>();
                cost.transform.position = obj.transform.position;
                cost.GetComponent<RectTransform>().offsetMin = obj.GetComponent<RectTransform>().offsetMin;
                cost.GetComponent<RectTransform>().offsetMax = obj.GetComponent<RectTransform>().offsetMax;
                cost.GetComponent<RectTransform>().anchorMin = obj.GetComponent<RectTransform>().anchorMin;
                cost.GetComponent<RectTransform>().anchorMax = obj.GetComponent<RectTransform>().anchorMax;
            }
            cost.ShowCost(new CostMultiple[] { new CostMultiple(new Item_Base[] { ___selectedBuildableItem }, 0) });
            cost.gameObject.SetActive(false);
        }

        static bool startedLocked = false;

        [HarmonyPatch(typeof(BlockCreator), "Update")]
        [HarmonyPostfix]
        static void BlockCreator_Update(BlockCreator __instance, BlockSurface ___quadSurface, Item_Base ___selectedBuildableItem, Block ___selectedBuildablePrefab)
        {
            for (int i = ghostBlocks.Count - 1; i >= 0; i--)
                if (!ghostBlocks[i])
                    ghostBlocks.RemoveAt(i);
            bool flag3 = __instance.selectedBlock && __instance.selectedBlock.gameObject.activeInHierarchy;
            bool flag4 = false;
            if (flag3 && Main.ExtraSettingsAPI_Loaded && MyInput.GetButton(Main.lockKey) && Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out var hit, Player.UseDistance * 2, LayerMasks.MASK_Block))
            {
                var b = hit.collider.GetComponentInParent<Block>();
                if (b?.buildableItem == __instance.selectedBlock.buildableItem)
                {
                    flag4 = true;
                    __instance.selectedBlock.transform.position = b.transform.position;
                    __instance.selectedBlock.transform.rotation = b.transform.rotation;
                }
            }
            bool flag1 = ___quadSurface != null && flag3;
            bool flag2 = Main.ExtraSettingsAPI_Loaded && MyInput.GetButton(Main.buildKey);
            if (__instance.selectedBlock && flag2)
            {
                if (ghostBlocks.Count == 0 && flag1)
                {
                    startedLocked = flag4;
                    ghostBlocks.Add(Object.Instantiate(___selectedBuildablePrefab, lockedPivot));
                    ghostBlocks[0].OnStartingPlacement();
                    ghostBlocks[0].transform.localPosition = __instance.selectedBlock.transform.localPosition;
                    cost.gameObject.SetActive(___selectedBuildableItem.settings_buildable.Placeable);
                }
            }
            else if (ghostBlocks.Count != 0)
            {
                int placed = 0;
                bool unlimited = GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources;
                dontClear = true;
                foreach (Block block in ghostBlocks)
                {
                    if (___selectedBuildableItem)
                    {
                        if (startedLocked)
                            startedLocked = false;
                        else if (!flag2 && flag3 && (___selectedBuildableItem.settings_buildable.Placeable ? cost.MeetsRequirements() : buildCost.MeetsRequirements()) && __instance.CanBuildBlock(block) && (unlimited || placed < placing))
                        {
                            __instance.PlaceBlock(___selectedBuildableItem, block.transform.localPosition, block.transform.localEulerAngles, ___quadSurface.dpsType);
                            placed++;
                        }
                    }
                    Object.Destroy(block?.gameObject);
                }
                dontClear = false;
                if ((___selectedBuildableItem && cost.costBoxes.Count != 0 && ___selectedBuildableItem.settings_buildable.Placeable ? cost.MeetsRequirements() : buildCost.MeetsRequirements()) && !unlimited)
                {
                    if (___selectedBuildableItem.settings_buildable.Placeable)
                        __instance.GetPlayerNetwork().Inventory.RemoveItem(___selectedBuildableItem.UniqueName, placed);
                    else
                        for (int i = 0; i < placed; i++)
                            __instance.GetPlayerNetwork().Inventory.RemoveCostMultiple(___selectedBuildableItem.settings_recipe.NewCost);
                }
                ghostBlocks.Clear();
                cost.gameObject.SetActive(false);
                if (placed > 0)
                    __instance.GetPlayerNetwork().Inventory.hotbar.ReselectCurrentSlot();
            }
            
            if (!__instance.selectedBlock)
                return;
            foreach (var block in ghostBlocks)
                block.gameObject.SetActive(flag1);
            foreach (var block in ghostBlocks)
            {
                block.transform.localRotation = __instance.selectedBlock.transform.localRotation;
                block.occupyingComponent?.SetNewMaterial(__instance.CanBuildBlock(block) ? materialGreen : materialRed);
            }
            if (flag1 && ghostBlocks.Count > 0)
            {
                var dir = ghostBlocks[0].transform.localPosition - __instance.selectedBlock.transform.localPosition;
                if (MyInput.GetButton("Sprint"))
                {
                    var x = Math.Abs(dir.x);
                    var y = Math.Abs(dir.y);
                    var z = Math.Abs(dir.z);
                    if (x >= y && x >= z)
                        dir = new Vector3(dir.x, 0, 0);
                    else if (y >= x && y >= z)
                        dir = new Vector3(0, dir.y, 0);
                    else
                        dir = new Vector3(0, 0, dir.z);
                }
                float dist;
                if (__instance.selectedBlock.snapsToQuads)
                {
                    var x = Math.Abs(dir.x / 1.5f);
                    var y = Math.Abs(dir.y / 1.21f);
                    var z = Math.Abs(dir.z / 1.5f);
                    if (x >= y && x >= z)
                        dist = 1.5f / Math.Abs(dir.normalized.x);
                    else if (y >= x && y >= z)
                        dist = 1.21f / Math.Abs(dir.normalized.y);
                    else
                        dist = 1.5f / Math.Abs(dir.normalized.z);
                }
                else
                {
                    ghostBlocks[0].SetOnOffColliderState(true);
                    dist = ghostBlocks[0].Closest(lockedPivot.TransformDirection(dir));
                    ghostBlocks[0].SetOnOffColliderState(false);
                }
                int blocks = 1;
                if (dist != 0)
                    blocks = (int)((dir.magnitude + (__instance.selectedBlock.snapsToQuads ? 0.1f : 0)) / dist) + 1;
                while (ghostBlocks.Count > blocks)
                {
                    Object.Destroy(ghostBlocks[1].gameObject);
                    ghostBlocks.RemoveAt(1);
                }
                int j = 0;
                for (int i = 1; i < blocks; i++)
                {
                    if (ghostBlocks.Count <= i)
                    {
                        var b = Object.Instantiate(___selectedBuildablePrefab, lockedPivot);
                        b.OnStartingPlacement();
                        b.transform.localRotation = ghostBlocks[0].transform.localRotation;
                        ghostBlocks.Add(b);
                    }
                    Vector3 pos = dir.normalized * dist * i;
                    if (__instance.selectedBlock.snapsToQuads)
                    {
                        pos.x = Mathf.Round(pos.x / 1.5f) * 1.5f;
                        pos.y = Mathf.Round(pos.y / 1.21f) * 1.21f;
                        pos.z = Mathf.Round(pos.z / 1.5f) * 1.5f;
                    }
                    ghostBlocks[i].transform.localPosition = ghostBlocks[0].transform.localPosition - pos;
                    if (!__instance.CanBuildBlock(ghostBlocks[i]))
                    {
                        ghostBlocks[i].occupyingComponent?.SetNewMaterial(materialRed);
                        j++;
                    }
                    else
                        ghostBlocks[i].occupyingComponent?.SetNewMaterial(materialGreen);
                }
                placing = blocks - j;
                if (startedLocked)
                    placing--;
                if (___selectedBuildableItem.settings_buildable.Placeable)
                {
                    cost.costBoxes[0].SetRequiredAmount(placing);
                    cost.gameObject.SetActive(true);
                }
                else
                {
                    buildCost.ShowCost(___selectedBuildableItem.settings_recipe.NewCost);
                    foreach (var box in buildCost.costBoxes)
                        box.SetRequiredAmount(Traverse.Create(box).Field("requiredAmount").GetValue<int>() * placing);
                }
            }
        }

        [HarmonyPatch(typeof(BlockCreator), "SetGhostBlockVisibility")]
        [HarmonyPostfix]
        static void BlockCreator_SetGhostBlockVisibility(bool visible)
        {
            if (!visible)
                foreach (Block block in ghostBlocks)
                    if (block != null)
                        block.gameObject.SetActive(false);
        }
    }
}