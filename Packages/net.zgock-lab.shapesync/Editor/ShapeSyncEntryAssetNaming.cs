// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Centralizes the final sub-asset names and Texture alias binding for one Figure/Entry pair.</summary>
    internal static class ShapeSyncEntryAssetNaming
    {
        internal static string GetTextureName(string figureName, string entryName) => GetPrefix(figureName, entryName);
        /// <summary>Returns the Figure/FBM Texture Resource name at a zero-based, owner-local Texture index.</summary>
        internal static string GetTextureName(string figureName, string entryName, int ownerTextureIndex)
        {
            if (ownerTextureIndex < 0) throw new ArgumentOutOfRangeException(nameof(ownerTextureIndex));
            string prefix = GetTextureName(figureName, entryName);
            return ownerTextureIndex == 0 ? prefix : prefix + "_" + (ownerTextureIndex + 1);
        }
        internal static string GetMaterialName(string figureName, string entryName) => GetPrefix(figureName, entryName) + "_Material";

        internal static void ApplyMaterialName(Material material, string figureName, string entryName)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            material.name = GetMaterialName(figureName, entryName);
        }

        /// <summary>Returns the shader MainTex used by Import All, or null when this Material has none.</summary>
        internal static Texture GetMainTexture(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            return material.mainTexture;
        }

        /// <summary>Enumerates distinct Material Textures with MainTex first and every remaining Texture in stable property-name order.</summary>
        internal static IEnumerable<Texture> GetTexturesMainTexFirst(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var emitted = new HashSet<Texture>();
            Texture mainTexture = GetMainTexture(material);
            if (mainTexture != null) { emitted.Add(mainTexture); yield return mainTexture; }
            foreach (string propertyName in material.GetTexturePropertyNames().OrderBy(name => name, StringComparer.Ordinal))
            {
                Texture texture = material.GetTexture(propertyName);
                if (texture != null && emitted.Add(texture)) yield return texture;
            }
        }

        /// <summary>Enumerates texture properties with the material MainTex property first.</summary>
        internal static IEnumerable<string> GetTexturePropertyNamesMainTexFirst(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            string[] properties = material.GetTexturePropertyNames()
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Texture mainTexture = GetMainTexture(material);
            string firstMainProperty = null;
            if (mainTexture != null)
            {
                foreach (string propertyName in properties)
                    if (material.GetTexture(propertyName) == mainTexture)
                    {
                        firstMainProperty = propertyName;
                        yield return propertyName;
                        break;
                    }
            }
            foreach (string propertyName in properties)
            {
                if (propertyName == firstMainProperty)
                {
                    continue;
                }
                yield return propertyName;
            }
        }

        /// <summary>Rebinds every shader property that aliases <paramref name="provisionalTexture"/> to the final Entry Texture.</summary>
        internal static void ReplaceTextureAliases(Material material, Texture provisionalTexture, Texture finalTexture)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (provisionalTexture == null) throw new ArgumentNullException(nameof(provisionalTexture));
            if (finalTexture == null) throw new ArgumentNullException(nameof(finalTexture));
            foreach (string propertyName in material.GetTexturePropertyNames())
                if (material.GetTexture(propertyName) == provisionalTexture) material.SetTexture(propertyName, finalTexture);
        }

        private static string GetPrefix(string figureName, string entryName)
        {
            if (string.IsNullOrWhiteSpace(figureName)) throw new ArgumentException("Figure name must not be empty.", nameof(figureName));
            if (string.IsNullOrWhiteSpace(entryName)) throw new ArgumentException("Entry name must not be empty.", nameof(entryName));
            return figureName + "_" + entryName;
        }
    }
}
#endif
