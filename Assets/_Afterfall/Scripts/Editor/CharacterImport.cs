using UnityEditor;

namespace Afterfall.EditorTools
{
    /// <summary>
    /// Import settings for the MPFB characters in Resources/Characters (tools/characters): skinned
    /// meshes without animation or materials (the game builds materials from each character's
    /// manifest), and textures capped at 1024 px with normal maps marked as such.
    /// </summary>
    public sealed class CharacterImport : AssetPostprocessor
    {
        private const string Folder = "Assets/_Afterfall/Resources/Characters/";

        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.importCameras = false;
            m.importLights = false;
            m.importVisibility = false;
            m.importBlendShapes = false;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.Generic;
            m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            m.optimizeGameObjects = false;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.isReadable = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.skinWeights = ModelImporterSkinWeights.Standard;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var t = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_normal.png", System.StringComparison.Ordinal);
            t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            t.sRGBTexture = !normal;
            t.alphaIsTransparency = !normal;
            t.mipmapEnabled = true;
            t.maxTextureSize = 1024;
            t.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
