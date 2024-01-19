using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;
using System;
using Steamworks;

public class RecursivePlacement : Mod
{
    Harmony harmony;
    public static string buildKey = "";
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
    }
    static Traverse ExtraSettingsAPI_Traverse;
    public static bool ExtraSettingsAPI_Loaded = false;
    public string ExtraSettingsAPI_GetKeybindName(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getKeybindName", new object[] { this, SettingName }).GetValue<string>();
        return "";
    }
}

public static class ExtentionMethods
{
    public static void PlaceBlock(this BlockCreator self, Item_Base blockItem, Vector3 position, Vector3 rotation, DPS dps)
    {
        if (Semih_Network.IsHost)
        {
            Message_BlockCreator_PlaceBlock message_BlockCreator_PlaceBlock = new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, self, blockItem.UniqueIndex, SaveAndLoad.GetUniqueObjectIndex(), SaveAndLoad.GetUniqueObjectIndex(), NetworkUpdateManager.GetUniqueBehaviourIndex(), position, rotation, -1, dps);
            ComponentManager<Semih_Network>.Value.RPC(message_BlockCreator_PlaceBlock, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
            self.CreateBlock(blockItem, message_BlockCreator_PlaceBlock.LocalPosition, message_BlockCreator_PlaceBlock.LocalEuler, message_BlockCreator_PlaceBlock.dpsType, -1, false, message_BlockCreator_PlaceBlock.blockObjectIndex, message_BlockCreator_PlaceBlock.networkedObjectIndex, message_BlockCreator_PlaceBlock.networkedBehaviourIndex);
            return;
        }
        Message_BlockCreator_PlaceBlock message = new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, self, blockItem.UniqueIndex, 0U, 0U, 0U, position, rotation, -1, dps);
        RAPI.GetLocalPlayer().SendP2P(message, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
    }

    public static BuildError GetBuildError(this BlockCreator self, Block block)
    {
        return Traverse.Create(self).Method("CanBuildBlock", new object[] { block }).GetValue<BuildError>();
    }

    public static bool CanBuildBlock(this BlockCreator self, Block block)
    {
        return self.GetBuildError(block) == BuildError.None;
    }

    public static float Closest(this Block block1, Vector3 direction)
    {
        direction = direction.normalized * 10;
        float minDistance = float.MaxValue;
        Dictionary<Collider, int> Layers = new Dictionary<Collider, int>();
        foreach (Collider a in block1.onoffColliders)
        {
            Layers.Add(a, a.gameObject.layer);
            a.gameObject.layer = 30;
        }
        foreach (Collider a in block1.onoffColliders)
        {
            Vector3 center = a.bounds.center;
            RaycastHit hit;
            if (
                a is BoxCollider && Physics.BoxCast(center - direction / 2, (a as BoxCollider).size / 2, direction, out hit, a.transform.rotation, direction.magnitude, (LayerMask)0x40000000) ||
                a is SphereCollider && Physics.SphereCast(center - direction / 2, (a as SphereCollider).radius, direction, out hit, direction.magnitude, (LayerMask)0x40000000) ||
                a is CapsuleCollider && Physics.CapsuleCast(center + Vector3.up * (a as CapsuleCollider).height / 2 - direction / 2, center - Vector3.up * (a as CapsuleCollider).height / 2 - direction / 2, (a as CapsuleCollider).radius, direction, out hit, direction.magnitude, (LayerMask)0x40000000)
                )
                if (hit.distance < minDistance)
                    minDistance = hit.distance;
        }
        foreach (Collider a in block1.onoffColliders)
            a.gameObject.layer = Layers[a];
        return direction.magnitude / 2 - minDistance;
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

    [HarmonyPatch(typeof(BlockCreator), "SetBlockTypeToBuild", new Type[] { typeof(Item_Base) })]
    [HarmonyPostfix]
    static void BlockCreator_SetBlockTypeToBuild(BlockCreator __instance, GameManager ___gameManager, Transform ___lockedBuildPivot, Item_Base ___selectedBuildableItem)
    {
        materialGreen = ___gameManager.ghostMaterialGreen;
        materialRed = ___gameManager.ghostMaterialRed;
        lockedPivot = ___lockedBuildPivot;
        foreach (Block block in ghostBlocks)
            GameObject.Destroy(block.gameObject);
        ghostBlocks.Clear();
        if (cost == null) 
        { 
            GameObject obj = Traverse.Create(ComponentManager<BuildMenu>.Value).Field("costColletionCursor").GetValue<CostCollection>().gameObject;
            cost = GameObject.Instantiate(obj, ComponentManager<CanvasHelper>.Value.transform).GetComponent<CostCollection>();
            cost.transform.position = obj.transform.position;
            cost.GetComponent<RectTransform>().offsetMin = obj.GetComponent<RectTransform>().offsetMin;
            cost.GetComponent<RectTransform>().offsetMax = obj.GetComponent<RectTransform>().offsetMax;
            cost.GetComponent<RectTransform>().anchorMin = obj.GetComponent<RectTransform>().anchorMin;
            cost.GetComponent<RectTransform>().anchorMax = obj.GetComponent<RectTransform>().anchorMax;
        }
        cost.ShowCost(new CostMultiple[] { new CostMultiple(new Item_Base[] { ___selectedBuildableItem }, 0) });
        cost.gameObject.SetActive(false);
    }

    [HarmonyPatch(typeof(BlockCreator), "Update")]
    [HarmonyPostfix]
    static void BlockCreator_Update(BlockCreator __instance, BlockSurface ___quadSurface, Item_Base ___selectedBuildableItem)
    {
        for (int i = ghostBlocks.Count - 1; i >= 0; i--)
            if (ghostBlocks[i] == null)
                ghostBlocks.RemoveAt(i);
        bool flag3 = __instance.selectedBlock != null && __instance.selectedBlock.gameObject.activeInHierarchy;
        bool flag1 = ___quadSurface != null && flag3;
        bool flag2 = RecursivePlacement.ExtraSettingsAPI_Loaded && MyInput.GetButton(RecursivePlacement.buildKey);
        if (__instance.selectedBlock != null && !__instance.selectedBlock.snapsToQuads && flag2)
        {
            if (ghostBlocks.Count == 0 && flag1)
            {
                ghostBlocks.Add(GameObject.Instantiate(__instance.selectedBlock, lockedPivot));
                ghostBlocks[0].transform.localPosition = __instance.selectedBlock.transform.localPosition;
                cost.gameObject.SetActive(true);
            }
        }
        else if (ghostBlocks.Count != 0)
        {
            int placed = 0;
            int holding = __instance.GetPlayerNetwork().Inventory.GetItemCount(___selectedBuildableItem.UniqueName);
            bool unlimited = GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources;
            foreach (Block block in ghostBlocks)
            {
                if (!flag2 && flag3 && cost.MeetsRequirements() && __instance.CanBuildBlock(block) && (unlimited || placed < holding))
                {
                    __instance.PlaceBlock(___selectedBuildableItem, block.transform.localPosition, block.transform.localEulerAngles, ___quadSurface.dpsType);
                    placed++;
                }
                GameObject.Destroy(block.gameObject);
            }
            if (cost.costBoxes.Count != 0 && cost.MeetsRequirements() && !unlimited)
                __instance.GetPlayerNetwork().Inventory.RemoveItem(___selectedBuildableItem.UniqueName, placed);
            ghostBlocks.Clear();
            cost.gameObject.SetActive(false);
            if (placed > 0 && __instance.GetPlayerNetwork().Inventory.GetSelectedHotbarSlot().HasValidItemInstance() && __instance.GetPlayerNetwork().Inventory.GetSelectedHotbarItem().settings_buildable.Placeable)
                __instance.SetBlockTypeToBuild(__instance.GetPlayerNetwork().Inventory.GetSelectedHotbarItem().UniqueName);
        }
        if (__instance.selectedBlock == null)
            return;
        foreach (Block block in ghostBlocks)
            block.gameObject.SetActive(flag1);
        foreach (Block block in ghostBlocks)
        {
            block.transform.localRotation = __instance.selectedBlock.transform.localRotation;
            block.occupyingComponent.SetNewMaterial(__instance.CanBuildBlock(block) ? materialGreen : materialRed);
        }
        if (flag1 && ghostBlocks.Count > 0)
        {
            Vector3 dir = ghostBlocks[0].transform.localPosition - __instance.selectedBlock.transform.localPosition;
            if (MyInput.GetButton("Sprint"))
            {
                var x = Math.Abs(dir.x);
                var y = Math.Abs(dir.y);
                var z = Math.Abs(dir.z);
                if (x >= y && x >= z)
                    dir = new Vector3(dir.x, 0, 0);
                else if(y >= x && y >= z)
                    dir = new Vector3(0, dir.y, 0);
                else
                    dir = new Vector3(0, 0, dir.z);
            }
            ghostBlocks[0].SetOnOffColliderState(true);
            float dist = ghostBlocks[0].Closest(lockedPivot.TransformDirection(dir));
            ghostBlocks[0].SetOnOffColliderState(false);
            int blocks = (int)(dir.magnitude / dist) + 1;
            while (ghostBlocks.Count > blocks)
            {
                GameObject.Destroy(ghostBlocks[1].gameObject);
                ghostBlocks.RemoveAt(1);
            }
            int j = 0;
            for (int i = 1; i < blocks; i++)
            {
                if (ghostBlocks.Count <= i)
                    ghostBlocks.Add(GameObject.Instantiate(ghostBlocks[0], lockedPivot));
                ghostBlocks[i].transform.localPosition = ghostBlocks[0].transform.localPosition - dir.normalized * dist * i;
                if (!__instance.CanBuildBlock(ghostBlocks[i]))
                    j++;
            }
            cost.costBoxes[0].SetRequiredAmount(blocks - j);
            cost.gameObject.SetActive(true);
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