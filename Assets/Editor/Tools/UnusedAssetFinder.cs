using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace OniBow.EditorTools
{
    public class UnusedAssetFinder : EditorWindow
    {
        private List<string> m_unusedAssets = new List<string>();
        private Vector2 m_scrollPosition;
        private bool m_isScanning = false;

        // 스크립트 파일은 리플렉션 사용 가능성 때문에 자동 삭제 대상에서 제외하는 것이 안전함
        private bool m_excludeScripts = true;

        [MenuItem("Tools/OniBow/Unused Asset Finder")]
        public static void ShowWindow()
        {
            GetWindow<UnusedAssetFinder>("Unused Assets");
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("Unused Asset Scanner", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            m_excludeScripts = GUILayout.Toggle(m_excludeScripts, "Exclude Source Scripts (.cs)");
            if (GUILayout.Button("Scan Project", GUILayout.Height(30)))
            {
                ScanForUnusedAssets();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (m_isScanning)
            {
                GUILayout.Label("Scanning... Please wait.");
                return;
            }

            if (m_unusedAssets != null && m_unusedAssets.Count > 0)
            {
                GUILayout.Label($"Found {m_unusedAssets.Count} unused assets:", EditorStyles.boldLabel);
                
                if (GUILayout.Button("Delete All Selected (Irreversible!)", GUILayout.ExpandWidth(false)))
                {
                    DeleteAllAssets();
                }

                GUILayout.Space(5);

                m_scrollPosition = GUILayout.BeginScrollView(m_scrollPosition);
                
                for (int i = 0; i < m_unusedAssets.Count; i++)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(m_unusedAssets[i], GUILayout.Width(position.width - 100));
                    
                    if (GUILayout.Button("Select", GUILayout.Width(50)))
                    {
                        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(m_unusedAssets[i]);
                    }

                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        AssetDatabase.DeleteAsset(m_unusedAssets[i]);
                        m_unusedAssets.RemoveAt(i);
                        i--;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("No unused assets found (or scan not started).");
            }
        }

        private void ScanForUnusedAssets()
        {
            m_isScanning = true;
            m_unusedAssets.Clear();

            try
            {
                // 1. 모든 에셋 경로 수집
                string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
                HashSet<string> usedAssets = new HashSet<string>();

                // 2. Root Set 수집 (Build Settings Scenes, Resources, Editor, StreamingAssets)
                List<string> rootAssets = new List<string>();

                // A. Build Settings의 씬
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    if (scene.enabled)
                    {
                        rootAssets.Add(scene.path);
                    }
                }

                // B. 특수 폴더들 (Resources, Editor, StreamingAssets)
                foreach (string path in allAssetPaths)
                {
                    if (path.Contains("/Resources/") || 
                        path.Contains("/Editor/") || 
                        path.Contains("/StreamingAssets/") ||
                        path.Contains("ProjectSettings/"))
                    {
                        rootAssets.Add(path);
                    }
                }

                // 3. 의존성 추적 (Dependencies)
                string[] dependencies = AssetDatabase.GetDependencies(rootAssets.ToArray(), true);
                foreach (string dep in dependencies)
                {
                    usedAssets.Add(dep);
                }

                // 4. 미사용 에셋 필터링
                foreach (string path in allAssetPaths)
                {
                    // 시스템 폴더 및 Packages 제외
                    if (path.StartsWith("Packages/") || path.StartsWith("ProjectSettings/"))
                        continue;

                    // 디렉토리는 제외 (파일만 처리)
                    if (AssetDatabase.IsValidFolder(path))
                        continue;

                    // 이미 사용중이면 패스
                    if (usedAssets.Contains(path))
                        continue;

                    // 옵션: 스크립트 제외
                    if (m_excludeScripts && path.EndsWith(".cs"))
                        continue;

                    m_unusedAssets.Add(path);
                }
            }
            finally
            {
                m_isScanning = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private void DeleteAllAssets()
        {
            if (EditorUtility.DisplayDialog("Delete All", 
                $"Are you sure you want to delete {m_unusedAssets.Count} assets?\nThis cannot be undone.", "Yes", "No"))
            {
                for (int i = 0; i < m_unusedAssets.Count; i++)
                {
                    AssetDatabase.DeleteAsset(m_unusedAssets[i]);
                }
                m_unusedAssets.Clear();
                AssetDatabase.Refresh();
            }
        }
    }
}
