/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.MetaWand.Editor.API;
using Meta.XR.MetaWand.Editor.Telemetry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Meta.XR.MetaWand.Editor
{
    [Serializable]
    internal class Prompt
    {
        public string Id { get; }
        public string PromptText { get; }

        public string ErrorMessage { get; private set; }

        public bool FailedToLoadPreGen { get; private set; }
        public string FailedToLoadPreGenErrorMessage { get; private set; }

        public bool Selected { get; set; }
        public Utils.GeneratorType GeneratorType { get; set; }

        public List<ContentPlaceholder> ContentPlaceholdersPreGenAssets { get; set; } = new();

        private readonly Dictionary<string, PromptHandler> _promptHandlers = new();

        public IReadOnlyCollection<Asset> Assets => _promptHandlers.Values.Select(p => p.Asset)
            .Where(a => a.IsReady)
            .ToList();

        private readonly MetaWandApiManager _apiManager;
        private readonly bool _showLodsSelector;


        public Prompt(string id, string promptText, MetaWandApiManager apiManager, bool showLodsSelector)
        {
            Id = id;
            PromptText = promptText;
            _apiManager = apiManager;
            _showLodsSelector = showLodsSelector;

            for (var i = 0; i < Constants.SearchResultQueryCount; i++)
            {
                var preGenKey = GetKey(i, true);
                var contentPlaceholder = new ContentPlaceholder(Utils.GeneratorType.Mesh, showLodsSelector, preGenKey);
                ContentPlaceholdersPreGenAssets.Add(contentPlaceholder);
                _promptHandlers.Add(preGenKey, new PromptHandler(_apiManager, this, contentPlaceholder, true));
            }

            _ = PopulatePreGenSlots();
        }

        public Prompt(PromptData data, MetaWandApiManager apiManager, bool showLodsSelector)
        {
            Id = data.Id;
            PromptText = data.Prompt;
            _apiManager = apiManager;
            _showLodsSelector = showLodsSelector;

            var index = 0;
            foreach (var asset in data.Assets)
            {
                var key = GetKey(index++, asset.IsPreGen);
                var contentPlaceholder = new ContentPlaceholder(GeneratorType, showLodsSelector, key);
                var promptHandler = new PromptHandler(_apiManager, this, contentPlaceholder, true);

                ContentPlaceholdersPreGenAssets.Add(contentPlaceholder);
                _ = promptHandler.LoadFromAsset(asset);
                _promptHandlers.Add(key, promptHandler);
            }
        }


        private async Task PopulatePreGenSlots()
        {
            var polyCount = Constants.DefaultModelSize;
            var result = await _apiManager.SearchAssets(PromptText, Constants.SearchResultQueryCount, polyCount, Id);
            if (!result.Success)
            {
                MetaWandEvent.Send(new MetaWandEvent.Data
                {
                    Name = Constants.Telemetry.EventNamePreviewsGenerated,
                    Entrypoint = Constants.Telemetry.EntrypointLoadState,
                    Target = Constants.Telemetry.TargetPreviewPanel,
                    Metadata = new Dictionary<string, string>
                    {
                        { Constants.Telemetry.ParamIsPregenResult, true.ToString() },
                        { Constants.Telemetry.ParamNumSuccessTiles, 0.ToString() },
                        { Constants.Telemetry.ParamNumErrorTiles, Constants.SearchResultQueryCount.ToString() },
                        { Constants.Telemetry.ParamSessionId, Id ?? string.Empty }
                    }
                });

                FailedToLoadPreGen = true;
                var permissionDeniedError = (result.ErrorSubCode != null &&
                                             Constants.ErrorCodes.TryGetValue(result.ErrorSubCode, out var errorType) &&
                                             errorType == Constants.ErrorPermissionDenied);
                FailedToLoadPreGenErrorMessage = permissionDeniedError
                    ? result.ErrorMessage
                    : Constants.ErrorMessageDefaultFailedToLoad;

                MetaWandEvent.Send(new MetaWandEvent.Data
                {
                    Name = Constants.Telemetry.EventNamePreGenerationFailure,
                    Entrypoint = Constants.Telemetry.EntrypointLoadState,
                    Target = Constants.Telemetry.TargetPreviewPanel,
                    IsEssential = true,
                    Metadata = new Dictionary<string, string>
                    {
                    }
                });
                return;
            }

            var genSearchTasks = new List<Task<bool>>();

            const int numberOfLods = 4;
            var assets = result.Results.Select(r => r.asset).ToList();
            for (var i = 0; i < Constants.SearchResultQueryCount; i++)
            {
                var key = GetKey(i, true);
                var promptHandler = _promptHandlers[key];
                if (_showLodsSelector && (assets[i].asset_metas == null || assets[i].asset_metas.First().all_polycounts.Length < numberOfLods))
                {
                    ContentPlaceholdersPreGenAssets.RemoveAt(i);
                    _promptHandlers.Remove(key);
                    continue;
                }

                genSearchTasks.Add(promptHandler.DownloadSearchAssetPreview(assets[i],
                    promptHandler.SetTextureContent));
            }

            var results = await Task.WhenAll(genSearchTasks);

            var successCount = results.Count(success => success);

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNamePreviewsGenerated,
                Entrypoint = Constants.Telemetry.EntrypointLoadState,
                Target = Constants.Telemetry.TargetPreviewPanel,
                IsEssential = true,
                Metadata = new Dictionary<string, string>
                {
                    { Constants.Telemetry.ParamIsPregenResult, true.ToString() },
                    { Constants.Telemetry.ParamNumSuccessTiles, successCount.ToString() },
                    { Constants.Telemetry.ParamNumErrorTiles, (results.Length - successCount).ToString() },
                    { Constants.Telemetry.ParamSessionId, Id ?? string.Empty }
                }
            });
        }

        private string GetKey(int index, bool isPreGen) => Constants.PreGenPrefix + $"{Id}_{index}";
    }

    internal class PromptHandler
    {
        private readonly MetaWandApiManager _apiManager;
        public Prompt Prompt { get; }
        private readonly ContentPlaceholder _contentPlaceholder;
        private Texture2D _previewImage;
        private bool _isPlaying;


        private readonly string _pathToPrefabs = Path.Combine(Constants.ParentFolderPath, Constants.AssetFolder, "Prefabs");
        private readonly string _pathToTextures = Path.Combine(Constants.ParentFolderPath, Constants.AssetFolder, "Textures");
        private readonly string _pathToMaterials = Path.Combine(Constants.ParentFolderPath, Constants.AssetFolder, "Materials");

        private GameObject _savedPrefab;
        private Asset _asset;
        public Asset Asset => _asset;

        private AudioClip _audioClip;
        private readonly bool _assetHasLods;

        public PromptHandler(MetaWandApiManager apiManager, Prompt prompt, ContentPlaceholder contentPlaceholder, bool assetHasLods)
        {
            _apiManager = apiManager;
            Prompt = prompt;
            _contentPlaceholder = contentPlaceholder;
            _contentPlaceholder.OnAddToSceneButton += OnAddToSceneButton;
            _contentPlaceholder.OnErrorButton += OnErrorButton;
            _contentPlaceholder.SetPromptHandler(this);
            _assetHasLods = assetHasLods;
        }


        // Add mesh to active scene
        private void OnAddToSceneButton()
        {
            _ = AddToScene();
        }

        private async Task AddToScene()
        {
            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameLinkClick,
                Entrypoint = Constants.Telemetry.EntrypointLoadState,
                Target = Constants.Telemetry.TargetAddToSceneButton,
                IsEssential = true,
                Metadata = new Dictionary<string, string>
                {
                    { Constants.Telemetry.ParamAssetId, Asset.AssetId },
                    { Constants.Telemetry.ParamIsPregenResult, _asset.IsPreGen.ToString() },
                    { Constants.Telemetry.ParamSessionId, Prompt.Id ?? string.Empty }
                }
            });

            if (_assetHasLods)
            {
                await FetchAndDownload(Asset.AssetId, Asset.Lods[_contentPlaceholder.SelectedLod]);
            }

            _ = PrefabUtility.InstantiatePrefab(_savedPrefab) as GameObject;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameObjectAddedToScene,
                Entrypoint = Constants.Telemetry.EntrypointLoadState,
                IsEssential = true,
                Metadata = new Dictionary<string, string>
                {
                    { Constants.Telemetry.ParamAssetId, Asset.AssetId },
                    { Constants.Telemetry.ParamIsPregenResult, _asset.IsPreGen.ToString() },
                    { Constants.Telemetry.ParamSessionId, Prompt.Id ?? string.Empty }
                }
            });
        }

        private async Task FetchAndDownload(string assetId, int lod)
        {
            if (_contentPlaceholder.GetState() is ContentState.Downloading or ContentState.Requesting)
            {
                return;
            }

            _contentPlaceholder.SetState(ContentState.Downloading);
            var result = await _apiManager.FetchLibraryAsset(assetId, lod, Prompt.Id);
            if (!result.Success)
            {
                _contentPlaceholder.SetState(ContentState.Error, result.ErrorMessage == Constants.Failure
                    ? Constants.SomethingWrong
                    : result.ErrorMessage, null, ShouldShowTryAgain(result.ErrorSubCode));

                MetaWandEvent.Send(new MetaWandEvent.Data
                {
                    Name = Constants.Telemetry.EventNamePreGenerationFailure,
                    Entrypoint = Constants.Telemetry.EntrypointLoadState,
                    Target = Constants.Telemetry.TargetAddToSceneButton,
                    IsEssential = true,
                    Metadata = new Dictionary<string, string>
                    {
                        { Constants.Telemetry.ParamSessionId, Prompt.Id ?? string.Empty }
                    }
                });
                return;
            }

            _asset.MeshUrl = result.AssetParts.First().mesh_urls.fbx;
            _asset.TextureUrl = result.AssetParts.First().texture_urls.First().albedo;
            _contentPlaceholder.SetState(ContentState.Generated);

            await DownloadAsset();
        }


        // Retry last operation
        private void OnErrorButton()
        {
            switch (_contentPlaceholder.GetPreviousState())
            {
                case ContentState.Downloading:
                    _ = RetryFailedDownload();
                    break;
                case ContentState.Downloaded or ContentState.Generated or ContentState.Saving:
                    _ = AddToScene();
                    break;
            }
        }

        private async Task RetryFailedDownload()
        {
            var isPreGen = Prompt.ContentPlaceholdersPreGenAssets.Any(c => c.Id == _contentPlaceholder.Id);
            // Failed to download the thumbnail
            if (_contentPlaceholder.PreviewImage == null)
            {
                _contentPlaceholder.SetState(isPreGen ? ContentState.Downloading : ContentState.Requesting);
                var preview = await Download(Asset.PreviewUrl, Asset.AssetId);
                if (preview.Length == 0)
                {
                    return;
                }

                SetTextureContent(preview);
                if (!isPreGen)
                {
                    _contentPlaceholder.SetState(ContentState.Preview, "", _previewImage);
                }
            }
            else // Failed to download the asset
            {
                _ = AddToScene();
            }
        }

        private async Task DownloadAsset()
        {
            if (_contentPlaceholder.GetState() is ContentState.Downloading or ContentState.Requesting)
            {
                return;
            }

            if (_apiManager == null || string.IsNullOrEmpty(Asset.MeshUrl))
            {
                _contentPlaceholder.SetState(ContentState.Error, "", null, false);
                return;
            }

            _contentPlaceholder.SetState(ContentState.Downloading, "Downloading...");

            var fbxUrl = Asset.MeshUrl;
            var textureUrl = Asset.TextureUrl;
            var fbx = await Download(fbxUrl, Asset.AssetId + "_fbx");
            var texture = await Download(textureUrl, Asset.AssetId + "_tex");

            if (fbx.Length == 0 || texture.Length == 0)
            {
                _contentPlaceholder.SetState(ContentState.Error);
                return;
            }

            SaveToProjectAsset(fbx, texture);
            _contentPlaceholder.SetState(ContentState.Downloaded, "", _previewImage);
        }

        public async Task<bool> DownloadSearchAssetPreview(SearchAsset asset, Action<byte[]> onDownloadComplete)
        {
            var assetPart = asset.asset_parts.Length > 0 ? asset.asset_parts[0] : null;
            var meshUrl = assetPart != null ? assetPart.mesh_urls.fbx : "";
            var textureUrl = assetPart != null && assetPart.texture_urls.Length > 0
                ? assetPart.texture_urls[0].albedo
                : "";
            _asset = new Asset
            {
                AssetId = asset.asset_id,
                PreviewUrl = asset.preview_urls.image,
                MeshUrl = meshUrl,
                TextureUrl = textureUrl,
                IsPreGen = true,
                Lods = asset.asset_metas.Any() ? asset.asset_metas.First().all_polycounts : Array.Empty<int>()
            };
            _contentPlaceholder.SetState(ContentState.Downloading, "Processing...");
            var result = await Download(Asset.PreviewUrl, Asset.AssetId);
            if (result.Length == 0)
            {
                return false;
            }

            onDownloadComplete?.Invoke(result);
            return true;
        }

        private string BuildDownloadCacheId()
        {
            return _assetHasLods
                ? Asset.AssetId + "_lod_" + GetLodString(_contentPlaceholder.SelectedLod)
                : "genai_image_cache";

        }

        private async Task<byte[]> Download(string url, string assetId)
        {
            using var progressDisplayer = new ScopedProgressDisplayer();
            progressDisplayer.OnProgressUpdate += OnProgressUpdate;
            var result = await _apiManager.DownloadAsset(url, assetId, progressDisplayer, BuildDownloadCacheId());
            if (!result.IsSuccess)
            {
                // Requesting state has completed, failed during thumbnail download state
                _contentPlaceholder.SetState(ContentState.Downloading);
                _contentPlaceholder.SetState(ContentState.Error);
                return Array.Empty<byte>();
            }

            return result.Content;
        }

        public void SetTextureContent(byte[] content)
        {
            _previewImage = new Texture2D(2, 2);
            _previewImage.LoadImage(content);
            _contentPlaceholder.SetState(ContentState.Generated, "", _previewImage);
        }

        private void OnProgressUpdate(float progress, long _) =>
            _contentPlaceholder.UpdateProgress(progress);

        private static bool ShouldShowTryAgain(string errorSubCode)
        {
            if (errorSubCode == null) return true;

            if (!Constants.ErrorCodes.TryGetValue(errorSubCode, out var error))
            {
                return true;
            }

            return error switch
            {
                Constants.ErrorLimitExceeded => true,
                Constants.ErrorInvalidContent => false,
                Constants.ErrorInvalidParam => false,
                Constants.ErrorPermissionDenied => false,
                Constants.ErrorUnexpectedError => true,
                _ => true
            };
        }


        #region File management

        private string GetLodString(int lod) => lod switch
        {
            0 => "XL",
            1 => "L",
            2 => "M",
            _ => "S"
        };

        private void SaveToProjectAsset(byte[] fbx, byte[] textures)
        {
            _contentPlaceholder.SetState(ContentState.Saving);
            CreateAssetFolders();
            if (_assetHasLods) CreateLodDirectories();

            var subDirName = Asset.AssetId;
            var assetName = _assetHasLods
                ? subDirName + "_" + GetLodString(_contentPlaceholder.SelectedLod)
                : Asset.AssetId;
            var fbxPath = _assetHasLods
                ? Path.Combine(_pathToPrefabs, subDirName, assetName + ".fbx")
                : Path.Combine(_pathToPrefabs, assetName + ".fbx");
            SaveBytesToAsset(fbx, fbxPath);

            var texPath = _assetHasLods
                ? Path.Combine(_pathToTextures, subDirName, assetName + ".png")
                : Path.Combine(_pathToTextures, assetName + ".png");
            SaveBytesToAsset(textures, texPath);

            var fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            if (fbxModel == null || texture == null)
            {
                _contentPlaceholder.SetState(ContentState.Error);
                return;
            }

            var instance = Object.Instantiate(fbxModel);
            var renderer = instance.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                var materialPath = _assetHasLods
                    ? Path.Combine(_pathToMaterials, subDirName, assetName + ".mat")
                    : Path.Combine(_pathToMaterials, assetName + ".mat");
                Material mat;

                var absolutePath = Path.GetFullPath(materialPath);
                if (!File.Exists(absolutePath))
                {
                    mat = new Material(renderer.sharedMaterial)
                    {
                        mainTexture = texture
                    };
                    AssetDatabase.CreateAsset(mat, materialPath);
                }
                else
                {
                    mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                }

                renderer.sharedMaterial = mat;
                AssetDatabase.ImportAsset(materialPath);
            }

            var prefabPath = _assetHasLods
                ? Path.Combine(_pathToPrefabs, subDirName, assetName + ".prefab")
                : Path.Combine(_pathToPrefabs, assetName + ".prefab");
            _savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            _asset.PrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }


        private void SaveBytesToAsset(byte[] bytes, string path)
        {
            var absolutePath = Path.GetFullPath(path);
            if (File.Exists(absolutePath))
            {
                return;
            }
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);
        }

        // Check if asset already downloaded and saved in project
        private bool AssetExist(out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(Asset.AssetId))
            {
                return false;
            }

            var prefabPath = Path.Combine(_pathToPrefabs, Asset.AssetId + ".prefab");
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefab != null;
        }

        private void CreateAssetFolders()
        {
            if (!AssetDatabase.IsValidFolder(Path.Combine(Constants.ParentFolderPath, Constants.AssetFolder)))
            {
                AssetDatabase.CreateFolder(Constants.ParentFolderPath, Constants.AssetFolder);
            }

            var path = Path.Combine(Constants.ParentFolderPath, Constants.AssetFolder);
            if (!AssetDatabase.IsValidFolder(_pathToPrefabs))
            {
                AssetDatabase.CreateFolder(path, "Prefabs");
            }

            if (!AssetDatabase.IsValidFolder(_pathToTextures))
            {
                AssetDatabase.CreateFolder(path, "Textures");
            }

            if (!AssetDatabase.IsValidFolder(_pathToMaterials))
            {
                AssetDatabase.CreateFolder(path, "Materials");
            }

        }

        private void CreateLodDirectories()
        {
            var dirName = Asset.AssetId;
            if (!AssetDatabase.IsValidFolder(Path.Combine(_pathToPrefabs, dirName)))
            {
                AssetDatabase.CreateFolder(_pathToPrefabs, dirName);
            }
            if (!AssetDatabase.IsValidFolder(Path.Combine(_pathToTextures, dirName)))
            {
                AssetDatabase.CreateFolder(_pathToTextures, dirName);
            }
            if (!AssetDatabase.IsValidFolder(Path.Combine(_pathToMaterials, dirName)))
            {
                AssetDatabase.CreateFolder(_pathToMaterials, dirName);
            }
        }

        public async Task LoadFromAsset(Asset asset)
        {
            _asset = asset;


            var bytes = await Download(_asset.PreviewUrl, _asset.AssetId);
            if (bytes.Length == 0)
            {
                return;
            }

            SetTextureContent(bytes);


            // Update downloaded prefab ref.
            if (string.IsNullOrEmpty(_asset.PrefabGuid))
            {
                return;
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(_asset.PrefabGuid);
            if (string.IsNullOrEmpty(prefabPath))
            {
                _contentPlaceholder.SetState(ContentState.Generated, "", _previewImage);
                return;
            }

            _savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            _contentPlaceholder.SetState(ContentState.Downloaded, "", _previewImage);
        }

        #endregion // File management
    }
}
