﻿using System;
using System.IO;
using System.Collections.Generic;
using Unity.HLODSystem.SpaceManager;
using Unity.HLODSystem.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.HLODSystem.Streaming
{
    class NotSupportStreaming : IStreamingBuilder
    {
        [InitializeOnLoadMethod]
        static void RegisterType()
        {
            StreamingBuilderTypes.RegisterType(typeof(NotSupportStreaming), -1);
        }

        private IGeneratedResourceManager m_manager;
        private SerializableDynamicObject m_streamingOptions;

        public NotSupportStreaming(IGeneratedResourceManager manager, SerializableDynamicObject streamingOptions)
        {
            m_manager = manager;
            m_streamingOptions = streamingOptions;
        }

        public void Build(SpaceNode rootNode, DisposableList<HLODBuildInfo> infos, GameObject root, float cullDistance, float lodDistance, Action<float> onProgress)
        {
            dynamic options = m_streamingOptions;
            string path = options.OutputDirectory;

            var defaultController = root.AddComponent<DefaultController>();
            HLODTreeNode convertedRootNode = ConvertNode(rootNode);

            if (onProgress != null)
                onProgress(0.0f);

            GameObject hlodRoot = new GameObject("HLODRoot");
            hlodRoot.transform.SetParent(root.transform, false);
            m_manager.AddGeneratedResource(hlodRoot);

            //I think it is better to do when convert nodes.
            //But that is not easy because of the structure.
            for (int i = 0; i < infos.Count; ++i)
            {
                var spaceNode = infos[i].Target;
                var hlodTreeNode = convertedTable[infos[i].Target];

                for (int oi = 0; oi < spaceNode.Objects.Count; ++oi)
                {
                    int highId = defaultController.AddHighObject(spaceNode.Objects[oi]);
                    hlodTreeNode.HighObjectIds.Add(highId);
                }

                GameObject go = WriteInfo(path, root.name, infos[i]);
                go.transform.SetParent(hlodRoot.transform, false);
                go.SetActive(false);
                int lowId = defaultController.AddLowObject(go);
                hlodTreeNode.LowObjectIds.Add(lowId);
                m_manager.AddGeneratedResource(go);

                if (onProgress != null)
                    onProgress((float) i / (float) infos.Count);
            }

            defaultController.Root = convertedRootNode;
            defaultController.CullDistance = cullDistance;
            defaultController.LODDistance = lodDistance;
        }

        Dictionary<SpaceNode, HLODTreeNode> convertedTable = new Dictionary<SpaceNode, HLODTreeNode>();

        private HLODTreeNode ConvertNode(SpaceNode rootNode)
        {
            HLODTreeNode root = new HLODTreeNode();

            Queue<HLODTreeNode> hlodTreeNodes = new Queue<HLODTreeNode>();
            Queue<SpaceNode> spaceNodes = new Queue<SpaceNode>();

            hlodTreeNodes.Enqueue(root);
            spaceNodes.Enqueue(rootNode);

            while (hlodTreeNodes.Count > 0)
            {
                var hlodTreeNode = hlodTreeNodes.Dequeue();
                var spaceNode = spaceNodes.Dequeue();

                convertedTable[spaceNode] = hlodTreeNode;

                hlodTreeNode.Bounds = spaceNode.Bounds;
                if (spaceNode.HasChild()!= null)
                {
                    List<HLODTreeNode> childTreeNodes = new List<HLODTreeNode>(spaceNode.GetChildCount());
                    for (int i = 0; i < spaceNode.GetChildCount(); ++i)
                    {
                        var treeNode = new HLODTreeNode();
                        childTreeNodes.Add(treeNode);

                        hlodTreeNodes.Enqueue(treeNode);
                        spaceNodes.Enqueue(spaceNode.GetChild(i));
                    }

                    hlodTreeNode.ChildNodes = childTreeNodes;

                }
            }

            return root;
        }

        private GameObject WriteInfo(string outputDir, string rootName, HLODBuildInfo info)
        {
            GameObject root = new GameObject();
            root.name = info.Name;
            
            for (int oi = 0; oi < info.WorkingObjects.Count; ++oi)
            {
                GameObject targetGO = root;
                WorkingObject wo = info.WorkingObjects[oi];
                string filenameWithoutExt = $"{outputDir}{rootName}{info.Name}";
                if (oi > 0)
                {
                    filenameWithoutExt += $"sub_{oi}";
                    targetGO = new GameObject();
                    targetGO.name = $"_{oi}";
                    targetGO.transform.SetParent(root.transform, false);
                }

                Mesh mesh = wo.Mesh.ToMesh();
                string meshFilename = filenameWithoutExt + ".mesh";
                AssetDatabase.CreateAsset(mesh, meshFilename);
                m_manager.AddGeneratedResource(mesh);
                
                List<Material> materials = new List<Material>();
                for (int mi = 0; mi < wo.Materials.Count; ++mi)
                {
                    WorkingMaterial wm = wo.Materials[mi];

                    if (wm.NeedWrite() == false)
                    {
                        materials.Add(wm.ToMaterial());
                        continue;
                    }
                    Material mat = new Material(wm.ToMaterial());
                    mat.EnableKeyword("_NORMALMAP");
                    string[] textureNames = wm.GetTextureNames();
                    string materialFilename = $"{filenameWithoutExt}.mat";
                    for (int ti = 0; ti < textureNames.Length; ++ti)
                    {
                        WorkingTexture wt = wm.GetTexture(textureNames[ti]);
                        Texture2D texture = wt.ToTexture();
                        byte[] pngBytes = texture.EncodeToPNG();
                        string textureFilename = $"{filenameWithoutExt}_{textureNames[ti]}.png";
                        
                        File.WriteAllBytes(textureFilename, pngBytes);

                        AssetDatabase.ImportAsset(textureFilename, ImportAssetOptions.ForceSynchronousImport);
                        
                        TextureImporter importer = AssetImporter.GetAtPath(textureFilename) as TextureImporter;
                        if (importer != null)
                        {
                            importer.textureType = wt.Type;
                            importer.wrapMode = wt.WrapMode;
                            importer.SaveAndReimport();
                        }

                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFilename);
                        mat.SetTexture(textureNames[ti], texture);

                        m_manager.AddGeneratedResource(texture);
                    }
                    AssetDatabase.CreateAsset(mat,materialFilename);
                    m_manager.AddGeneratedResource(mat);
                    materials.Add(mat);
                    
                }

                targetGO.AddComponent<MeshFilter>().sharedMesh = mesh;
                targetGO.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            }

            return root;
        }
        
        
        public static void OnGUI(SerializableDynamicObject streamingOptions)
        {
            dynamic options = streamingOptions;

            if (options.OutputDirectory == null)
            {
                string path = Application.dataPath;
                path = "Assets" + path.Substring(Application.dataPath.Length);
                path = path.Replace('\\', '/');
                if (path.EndsWith("/") == false)
                    path += "/";
                options.OutputDirectory = path;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("OutputDirectory");
            if (GUILayout.Button(options.OutputDirectory))
            {
                string selectPath = EditorUtility.OpenFolderPanel("Select output folder", "Assets", "");

                if (selectPath.StartsWith(Application.dataPath))
                {
                    selectPath = "Assets" + selectPath.Substring(Application.dataPath.Length);
                    selectPath = selectPath.Replace('\\', '/');
                    if (selectPath.EndsWith("/") == false)
                        selectPath += "/";
                    options.OutputDirectory = selectPath;
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"Select directory under {Application.dataPath}", "OK");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

    }
}
