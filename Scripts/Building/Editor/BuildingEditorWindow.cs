using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Data.Building;
using Fusion;
using Placeable;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using SObject;
using UnityEditor;
using UnityEngine;

namespace Building.Editor
{
	public class BuildingMasterEditorWindow : OdinEditorWindow
	{
		#region UI Drawing Methods

		private void DrawFaceSelectionGrid()
		{
			EditorGUILayout.LabelField("Select Face to Edit", EditorStyles.boldLabel);

			var faceData = new (string name, string icon, int index)[]
			{
				("Top", "▲", 0), ("Bottom", "▼", 1),
				("Right", "►", 2), ("Left", "◄", 3),
				("Front", "●", 4), ("Back", "○", 5)
			};

			for (var row = 0; row < 3; row++)
			{
				EditorGUILayout.BeginHorizontal();
				for (var col = 0; col < 2; col++)
				{
					var idx = row * 2 + col;
					var (name, icon, index) = faceData[idx];
					var config = _currentEditingData.GetFaceConfig(index);
					var isSelected = _selectedFaceIndex == index;

					var style = new GUIStyle(GUI.skin.button)
					{
						fixedHeight = 60,
						fontSize = isSelected ? 14 : 12,
						fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal
					};

					if (!config.Generate)
						GUI.backgroundColor = Color.gray;
					else if (isSelected)
						GUI.backgroundColor = GetColorForSnapType(config.Type);
					else
						GUI.backgroundColor = GetColorForSnapType(config.Type) * 0.7f;

					var content = config.Generate
						? $"{icon} {name}\n{config.Type} ({config.SnapPoints.Count})"
						: $"{icon} {name}\n(Disabled)";

					if (GUILayout.Button(content, style))
					{
						if (Event.current.button == 0)
						{
							if (_selectedFaceIndex == index)
							{
								_selectedFaceIndex = -1;
								_selectedFaceConfig = null;
							}
							else
							{
								_selectedFaceIndex = index;
								_selectedFaceConfig = config;
							}
						}
						else if (Event.current.button == 1)
						{
							config.Generate = !config.Generate;

							if (!config.Generate && _selectedFaceIndex == index)
							{
								_selectedFaceIndex = -1;
								_selectedFaceConfig = null;
							}
							else if (config.Generate && _selectedFaceIndex < 0)
							{
								_selectedFaceIndex = index;
								_selectedFaceConfig = config;
							}

							RequestRepaint();
						}
					}

					GUI.backgroundColor = Color.white;
				}

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.HelpBox("Left click: Select face | Right click: Enable/Disable", MessageType.None);
		}

		#endregion

		#region Constants & Static Paths

		private const string DefaultPrefabSavePath = "Assets/Prefabs/Building3DModels/Generated";
		private const string DefaultDataSavePath = "Assets/Resources/Buildings/Data";
		private const string SnapConfigSaveDir = "Assets/Resources/Buildings/Snap";

		#endregion

		#region Core Data & State

		[BoxGroup("Current Target Data", order: -1)]
		[InfoBox(
			"This is the base data for all editing operations. Generate it in 'Step 1' or assign it directly to begin.")]
		[OnValueChanged("OnTargetDataChanged")]
		[LabelWidth(120)]
		public PlaceableData targetData;

		private PreviewRenderUtility _previewRenderUtility;
		private Vector2 _previewDir = new(120, -20);
		private float _previewZoom = 1.0f;
		private Vector2 _previewPan = Vector2.zero;
		private bool _needsRepaint = false;

		#endregion

		#region Window Management

		[MenuItem("Tools/Building System/Building Editor")]
		public static void ShowWindow()
		{
			GetWindow<BuildingMasterEditorWindow>("Building Editor").Show();
		}

		[Title("Master Control")]
		[Button("Start New Build Process", ButtonSizes.Large, Icon = SdfIconType.PlusSquareDotted)]
		[PropertyOrder(-100)]
		private void StartNewProcess()
		{
			if (EditorUtility.DisplayDialog("Clear All Fields?",
				    "This will reset all working data. Are you sure you want to start a new building creation process?",
				    "Yes, Start New", "Cancel"))
				ClearAllFields();
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			SetupPreviewUtility();
			ApplyGeneratorPreset();
			FindDefaultMaterials();
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			_previewRenderUtility?.Cleanup();
		}

		#endregion

		// =============================================================================================================
		// STEP 1: ASSET GENERATION
		// =============================================================================================================

		#region Tab 1: Generate Assets

		[TabGroup("MainTabs", "Step 1: Generate Assets", Order = 1)]
		[BoxGroup("MainTabs/Step 1: Generate Assets/Settings")]
		[Title("Generator Settings")]
		[LabelWidth(140)]
		public GeneratorPreset genPreset;

		[TabGroup("MainTabs", "Step 1: Generate Assets")]
		[BoxGroup("MainTabs/Step 1: Generate Assets/Model")]
		[Title("Source Model")]
		[Required("A base model prefab must be assigned")]
		[OnValueChanged("OnBaseModelChanged")]
		[LabelWidth(140)]
		public GameObject genBaseModel;

		[TabGroup("MainTabs", "Step 1: Generate Assets")]
		[BoxGroup("MainTabs/Step 1: Generate Assets/Model")]
		[LabelWidth(140)]
		[ValidateInput("ValidateItemName", "Item name cannot be empty")]
		public string genItemName = "NewBuilding";

		[TabGroup("MainTabs", "Step 1: Generate Assets")]
		[BoxGroup("MainTabs/Step 1: Generate Assets/Model")]
		[ShowIf("ShouldShowDefaultColliderType")]
		[EnumToggleButtons]
		[LabelWidth(140)]
		public DefaultColliderType defaultColliderType = DefaultColliderType.Box;

		[TabGroup("MainTabs", "Step 1: Generate Assets")]
		[BoxGroup("MainTabs/Step 1: Generate Assets/Actions")]
		[Button("Generate All Assets & Set as Target", ButtonSizes.Large)]
		[GUIColor(0.4f, 1f, 0.4f)]
		[EnableIf("@this.genBaseModel != null && !string.IsNullOrEmpty(this.genItemName)")]
		[PropertySpace(SpaceBefore = 10)]
		private void GenerateInitialAssetsAndProceed()
		{
			var dataAssetPath = Path.Combine(genDataSavePath, $"Data_{genItemName}.asset");
			GenerateInitialAssets(dataAssetPath);

			var generatedData = AssetDatabase.LoadAssetAtPath<PlaceableData>(dataAssetPath);
			if (generatedData == null)
			{
				Debug.LogError("Failed to load PlaceableData after asset generation.");
				return;
			}

			targetData = generatedData;
			OnTargetDataChanged();

			EditorUtility.DisplayDialog("Step 1 Complete",
				"Asset generation finished. The new asset is now the 'Current Target Data'.\nProceed to 'Step 2: Edit Snap Points'.",
				"OK");
		}

		// Hidden settings
		[HideInInspector] public Material genPreviewGreenMaterial;
		[HideInInspector] public Material genPreviewRedMaterial;
		[HideInInspector] public BuildingPartType genBuildingPartType;
		[HideInInspector] public LayerMask genPlacementMask;
		[HideInInspector] public LayerMask genCollisionMask;
		[HideInInspector] public string genPreviewObjectLayer;
		[HideInInspector] public string genNetworkObjectLayer;
		[HideInInspector] public string genPrefabSavePath = DefaultPrefabSavePath;
		[HideInInspector] public string genDataSavePath = DefaultDataSavePath;

		public enum DefaultColliderType
		{
			Box,
			Mesh
		}

		private bool ValidateItemName(string itemName, ref string errorMessage)
		{
			if (!string.IsNullOrWhiteSpace(itemName)) return true;
			errorMessage = "Item name cannot be empty";
			return false;
		}

		#endregion

		// =============================================================================================================
		// STEP 2: SNAP POINT EDITING
		// =============================================================================================================

		#region Tab 2: Edit Snap Points

		[HideInInspector] public BuildingSystemSettings snapSettings;
		[HideInInspector] public GameObject snapPreviewTarget;
		[HideInInspector] public List<BuildingPartPreset> snapPresets;

		private SnapEditingData _currentEditingData;
		private int _selectedFaceIndex = -1;
		private FaceSnapConfig _selectedFaceConfig;
		private int _selectedPointIndex = -1;
		private bool _showSnapTolerance;

		// 2D Face Editor variables
		private Vector2 _2DScrollPosition;
		private float _2DZoom = 1.0f;
		private Vector2 _2DPan = Vector2.zero;
		private readonly float _gridSize = 20f; // pixels per unit
		private bool _isSnappingEnabled = true;

		[TabGroup("MainTabs", "Step 2: Edit Snap Points", Order = 2)]
		[ShowIf("@this.targetData != null")]
		[HorizontalGroup("MainTabs/Step 2: Edit Snap Points/Split", 0.5f)]
		[VerticalGroup("MainTabs/Step 2: Edit Snap Points/Split/Left")]
		[OnInspectorGUI]
		private void DrawSnapEditor()
		{
			if (_currentEditingData == null || snapPreviewTarget == null) return;

			// Face selection
			SirenixEditorGUI.Title("Select Face to Edit", "", TextAlignment.Left, true);
			DrawFaceSelectionGrid();
			EditorGUILayout.Space(15);

			// 2D Face Editor
			if (_selectedFaceIndex >= 0 && _selectedFaceConfig != null)
			{
				SirenixEditorGUI.Title($"2D Face Editor - {GetFaceName(_selectedFaceIndex)}", "", TextAlignment.Left,
					true);

				// Controls
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Reset View", EditorStyles.miniButton, GUILayout.Width(80)))
				{
					_2DZoom = 1.0f;
					_2DPan = Vector2.zero;
				}

				_showSnapTolerance = GUILayout.Toggle(_showSnapTolerance, "Show Tolerance", EditorStyles.miniButton,
					GUILayout.Width(100));
				_isSnappingEnabled = GUILayout.Toggle(_isSnappingEnabled, "Enable Snapping", EditorStyles.miniButton,
					GUILayout.Width(110));

				EditorGUILayout.EndHorizontal();

				EditorGUILayout.HelpBox(
					"Left Click: Add point | Right Click: Remove nearest | Scroll: Zoom | Middle Drag: Pan",
					MessageType.Info);

				// 2D Face View
				var faceViewRect = GUILayoutUtility.GetRect(400, 400, GUILayout.ExpandWidth(true));
				Draw2DFaceEditor(faceViewRect);

				// Point List
				DrawPointList();
			}
			else
			{
				EditorGUILayout.HelpBox("Select a face above to start editing its snap points.", MessageType.Info);
			}
		}

		// [교체] Draw2DFaceEditor
		private void Draw2DFaceEditor(Rect rect)
		{
			// --- 1. 배경 및 기본 설정 ---
			EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1.0f));
			var e = Event.current;

			// Face Size 계산
			if (!TryGetBounds(snapPreviewTarget, out _, out var extents)) return;
			var faceSize = GetFaceSize(_selectedFaceIndex, extents);

			// --- 2. 입력 처리 ---
			// 마우스가 현재 2D 에디터 영역 안에 있을 때만 입력을 받음
			if (rect.Contains(e.mousePosition))
				switch (e.type)
				{
					// 줌 (Scroll Wheel)
					case EventType.ScrollWheel:
					{
						var zoomDelta = 1f - e.delta.y * 0.05f;
						var oldZoom = _2DZoom;
						_2DZoom = Mathf.Clamp(_2DZoom * zoomDelta, 0.1f, 5f);

						// 마우스 위치 기준으로 줌
						var mouseLocal = e.mousePosition - rect.center - _2DPan;
						_2DPan -= mouseLocal * (_2DZoom / oldZoom - 1f);

						e.Use(); // 이벤트를 여기서 사용했음을 명시
						break;
					}
					// 이동 (Pan)
					case EventType.MouseDrag when e.button == 2:
						_2DPan += e.delta;
						e.Use();
						break;
					// 포인트 추가/삭제 (Click)
					case EventType.MouseDown:
						var localMousePos = ScreenToFaceLocal(e.mousePosition, rect);
						var snappedLocalMousePos = _isSnappingEnabled
							? GetSnappedPosition(localMousePos, faceSize)
							: localMousePos;

						if (e.button == 0) // 왼쪽 클릭 (스냅된 위치에 추가)
							AddPointAtPosition(snappedLocalMousePos);
						else if (e.button == 1) // 오른쪽 클릭 (스냅된 위치 기준 삭제)
							RemoveNearestPoint(snappedLocalMousePos);

						e.Use();
						break;
				}

			// --- 3. 그리기 ---
			// 이벤트 타입이 Repaint일 때만 그리기 로직 실행
			if (e.type == EventType.Repaint)
			{
				DrawGrid(rect);
				DrawFaceBounds(rect);
				DrawSnapPointsIn2D(rect);
			}

			// 스냅 미리보기 마커 그리기 (마우스가 위에 있을 때만)
			if (_isSnappingEnabled && rect.Contains(e.mousePosition))
			{
				var rawLocalMousePos = ScreenToFaceLocal(e.mousePosition, rect);
				var snappedLocalMousePos = GetSnappedPosition(rawLocalMousePos, faceSize);
				var screenSnapPos = FaceLocalToScreen(snappedLocalMousePos, rect);

				Handles.BeginGUI();
				Handles.color = Color.yellow;
				Handles.DrawLine(new Vector3(screenSnapPos.x - 5, screenSnapPos.y),
					new Vector3(screenSnapPos.x + 5, screenSnapPos.y));
				Handles.DrawLine(new Vector3(screenSnapPos.x, screenSnapPos.y - 5),
					new Vector3(screenSnapPos.x, screenSnapPos.y + 5));
				Handles.EndGUI();
			}

			// 변경 사항이 있으면 창을 다시 그리도록 요청
			RequestRepaint();
		}

		private void DrawGrid(Rect rect)
		{
			var gridSpacing = _gridSize * _2DZoom;
			var gridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

			Handles.BeginGUI();
			Handles.color = gridColor;

			var center = rect.center + _2DPan;

			// Vertical lines
			var startX = rect.x;
			var endX = rect.xMax;
			var x = center.x % gridSpacing;
			while (x < endX)
			{
				if (x > startX)
					Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
				x += gridSpacing;
			}

			// Horizontal lines
			var startY = rect.y;
			var endY = rect.yMax;
			var y = center.y % gridSpacing;
			while (y < endY)
			{
				if (y > startY)
					Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
				y += gridSpacing;
			}

			// Center axes
			Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
			Handles.DrawLine(new Vector3(center.x, rect.y), new Vector3(center.x, rect.yMax));
			Handles.DrawLine(new Vector3(rect.x, center.y), new Vector3(rect.xMax, center.y));

			Handles.EndGUI();
		}

		private void DrawFaceBounds(Rect rect)
		{
			if (!TryGetBounds(snapPreviewTarget, out _, out var extents))
				return;

			var faceSize = GetFaceSize(_selectedFaceIndex, extents);
			var halfSize = faceSize * 0.5f * _gridSize * _2DZoom;
			var center = rect.center + _2DPan;

			var boundsRect = new Rect(center - halfSize, halfSize * 2f);

			Handles.BeginGUI();
			Handles.color = Color.white;
			Handles.DrawLine(new Vector3(boundsRect.xMin, boundsRect.yMin),
				new Vector3(boundsRect.xMax, boundsRect.yMin));
			Handles.DrawLine(new Vector3(boundsRect.xMax, boundsRect.yMin),
				new Vector3(boundsRect.xMax, boundsRect.yMax));
			Handles.DrawLine(new Vector3(boundsRect.xMax, boundsRect.yMax),
				new Vector3(boundsRect.xMin, boundsRect.yMax));
			Handles.DrawLine(new Vector3(boundsRect.xMin, boundsRect.yMax),
				new Vector3(boundsRect.xMin, boundsRect.yMin));
			Handles.EndGUI();

			// Draw size info
			EditorGUI.LabelField(new Rect(boundsRect.x, boundsRect.yMax + 5, 200, 20),
				$"Size: {faceSize.x:F2} x {faceSize.y:F2}", EditorStyles.miniLabel);
		}

		private void DrawSnapPointsIn2D(Rect rect)
		{
			if (_selectedFaceConfig?.SnapPoints == null) return;

			for (var i = 0; i < _selectedFaceConfig.SnapPoints.Count; i++)
			{
				var point = _selectedFaceConfig.SnapPoints[i];
				var screenPos = FaceLocalToScreen(GetPointLocalPosition(point, _selectedFaceIndex), rect);

				var color = GetColorForSnapType(point.type);
				var size = i == _selectedPointIndex ? 12f : 8f;

				// Draw tolerance circle if enabled
				if (_showSnapTolerance)
				{
					var toleranceRadius = point.maxPenetration * _gridSize * _2DZoom;
					var toleranceColor = color * 0.3f;
					toleranceColor.a = 0.2f;
					EditorGUI.DrawRect(
						new Rect(screenPos - Vector2.one * toleranceRadius, Vector2.one * toleranceRadius * 2),
						toleranceColor);
				}

				// Draw point
				EditorGUI.DrawRect(new Rect(screenPos - Vector2.one * size * 0.5f, Vector2.one * size), color);

				// Draw index
				GUI.Label(new Rect(screenPos.x + size / 2, screenPos.y - size / 2, 20, 20), i.ToString(),
					EditorStyles.miniLabel);
			}
		}

		private void DrawPointList()
		{
			if (_selectedFaceConfig?.SnapPoints == null) return;

			EditorGUILayout.Space();
			SirenixEditorGUI.Title("Snap Points", "", TextAlignment.Left, true);

			var list = _selectedFaceConfig.SnapPoints;

			if (list.Count == 0)
			{
				EditorGUILayout.HelpBox("No snap points on this face. Click in the 2D view to add points.",
					MessageType.Info);
				return;
			}

			EditorGUILayout.BeginVertical(GUI.skin.box);

			for (var i = 0; i < list.Count; i++)
			{
				var isSelected = _selectedPointIndex == i;
				var originalColor = GUI.backgroundColor;
				if (isSelected)
					GUI.backgroundColor = Color.yellow * 0.8f;

				EditorGUILayout.BeginHorizontal();

				// Point info
				EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(30));

				// Type selector
				list[i].type = (SnapType)EditorGUILayout.EnumPopup(list[i].type, GUILayout.Width(100));

				// Position (read-only)
				var localPos = GetPointLocalPosition(list[i], _selectedFaceIndex);
				EditorGUILayout.LabelField($"({localPos.x:F2}, {localPos.y:F2})", GUILayout.Width(80));

				// Tolerance
				list[i].maxPenetration = EditorGUILayout.FloatField(list[i].maxPenetration, GUILayout.Width(50));

				// Select button
				if (GUILayout.Button(isSelected ? "Deselect" : "Select", GUILayout.Width(60)))
				{
					_selectedPointIndex = isSelected ? -1 : i;
					RequestRepaint();
				}

				// Delete button
				if (GUILayout.Button("X", GUILayout.Width(25)))
				{
					list.RemoveAt(i);
					if (_selectedPointIndex >= i) _selectedPointIndex--;
					RequestRepaint();
					break;
				}

				EditorGUILayout.EndHorizontal();
				GUI.backgroundColor = originalColor;
			}

			EditorGUILayout.EndVertical();

			if (GUILayout.Button("Clear All Points"))
				if (EditorUtility.DisplayDialog("Confirm Clear",
					    "Remove all snap points from this face?", "Yes", "No"))
				{
					list.Clear();
					_selectedPointIndex = -1;
					RequestRepaint();
				}
		}

		[HorizontalGroup("MainTabs/Step 2: Edit Snap Points/Split")]
		[VerticalGroup("MainTabs/Step 2: Edit Snap Points/Split/Right")]
		[BoxGroup("MainTabs/Step 2: Edit Snap Points/Split/Right/Preview")]
		[ShowIf("ShowSnapPreview")]
		[OnInspectorGUI]
		private void Draw3DPreview()
		{
			EditorGUILayout.BeginHorizontal();
			SirenixEditorGUI.Title("3D Preview (Read-Only)", "", TextAlignment.Left, true);

			if (GUILayout.Button("Reset View", EditorStyles.miniButton, GUILayout.Width(80)))
			{
				_previewDir = new Vector2(120, -20);
				_previewZoom = 1.0f;
				_previewPan = Vector2.zero;
				RequestRepaint();
			}

			EditorGUILayout.EndHorizontal();

			EditorGUILayout.LabelField("Left drag: Rotate • Middle drag: Pan • Scroll: Zoom",
				EditorStyles.centeredGreyMiniLabel);

			var previewRect =
				GUILayoutUtility.GetRect(400, 500, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f, 1.0f));

			Draw3DPreviewReadOnly(previewRect);
		}

		private void Draw3DPreviewReadOnly(Rect rect)
		{
			if (snapPreviewTarget == null)
			{
				EditorGUI.LabelField(rect, "No preview target", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			var e = Event.current;

			// Handle camera controls only
			if (rect.Contains(e.mousePosition))
				switch (e.type)
				{
					case EventType.MouseDrag:
						if (e.button == 0) // Rotate
						{
							_previewDir.x += e.delta.x * 0.5f;
							_previewDir.y -= e.delta.y * 0.5f;
						}
						else if (e.button == 2) // Pan
						{
							_previewPan.x -= e.delta.x * 0.01f;
							_previewPan.y += e.delta.y * 0.01f;
						}

						e.Use();
						RequestRepaint();
						break;

					case EventType.ScrollWheel:
						_previewZoom *= 1f - e.delta.y * 0.03f;
						_previewZoom = Mathf.Clamp(_previewZoom, 0.2f, 3.0f);
						e.Use();
						RequestRepaint();
						break;
				}

			if (e.type == EventType.Repaint) RenderPreview(rect);

			// Draw all snap points from all faces
			DrawAllSnapPointsOverlay(rect);
		}

		private void DrawAllSnapPointsOverlay(Rect rect)
		{
			if (_currentEditingData == null || snapPreviewTarget == null) return;

			Handles.BeginGUI();

			// Draw points from all faces
			for (var faceIdx = 0; faceIdx < 6; faceIdx++)
			{
				var faceConfig = _currentEditingData.GetFaceConfig(faceIdx);
				if (faceConfig?.SnapPoints == null) continue;

				var isSelectedFace = faceIdx == _selectedFaceIndex;

				foreach (var snapPoint in faceConfig.SnapPoints)
				{
					var worldPos = snapPreviewTarget.transform.TransformPoint(snapPoint.localPosition);
					var screenPos = WorldToScreenPoint(rect, worldPos);

					if (screenPos.HasValue)
					{
						var color = GetColorForSnapType(snapPoint.type);
						if (!isSelectedFace)
							color.a = 0.3f;

						var size = isSelectedFace ? 8f : 5f;
						var pointRect = new Rect(screenPos.Value - Vector2.one * size * 0.5f, Vector2.one * size);
						EditorGUI.DrawRect(pointRect, color);
					}
				}
			}

			Handles.EndGUI();
		}

		// Helper methods for 2D face editing
		private Vector2 GetPointLocalPosition(SnapPointConfigData point, int faceIndex)
		{
			// Convert 3D local position to 2D face coordinates
			var localPos = point.localPosition;

			return faceIndex switch
			{
				0 or 1 => new Vector2(localPos.x, localPos.z), // Top/Bottom (Y faces)
				2 or 3 => new Vector2(localPos.z, localPos.y), // Right/Left (X faces)
				4 or 5 => new Vector2(localPos.x, localPos.y), // Front/Back (Z faces)
				_ => Vector2.zero
			};
		}

		private Vector3 ConvertTo3DPosition(Vector2 faceLocal, int faceIndex)
		{
			if (!TryGetBounds(snapPreviewTarget, out var center, out var extents))
				return Vector3.zero;

			// Convert 2D face coordinates to 3D local position
			var localPos = Vector3.zero;

			switch (faceIndex)
			{
				case 0: // Top (Y+)
					localPos = new Vector3(faceLocal.x, extents.y, faceLocal.y) + center;
					break;
				case 1: // Bottom (Y-)
					localPos = new Vector3(faceLocal.x, -extents.y, faceLocal.y) + center;
					break;
				case 2: // Right (X+)
					localPos = new Vector3(extents.x, faceLocal.y, faceLocal.x) + center;
					break;
				case 3: // Left (X-)
					localPos = new Vector3(-extents.x, faceLocal.y, faceLocal.x) + center;
					break;
				case 4: // Front (Z+)
					localPos = new Vector3(faceLocal.x, faceLocal.y, extents.z) + center;
					break;
				case 5: // Back (Z-)
					localPos = new Vector3(faceLocal.x, faceLocal.y, -extents.z) + center;
					break;
			}

			return localPos;
		}

		private Vector2 ScreenToFaceLocal(Vector2 screenPos, Rect rect)
		{
			var center = rect.center + _2DPan;
			var localScreen = screenPos - center;
			return localScreen / (_gridSize * _2DZoom);
		}

		private Vector2 FaceLocalToScreen(Vector2 faceLocal, Rect rect)
		{
			var center = rect.center + _2DPan;
			return center + faceLocal * _gridSize * _2DZoom;
		}

		private void AddPointAtPosition(Vector2 faceLocal)
		{
			if (_selectedFaceConfig?.SnapPoints == null) return;

			var localPos3D = ConvertTo3DPosition(faceLocal, _selectedFaceIndex);
			var normal = GetFaceNormal(_selectedFaceIndex);

			var newPoint = new SnapPointConfigData
			{
				localPosition = localPos3D,
				localRotation = Quaternion.LookRotation(normal),
				type = _selectedFaceConfig.Type,
				snapRadius = 0.5f,
				maxPenetration = 0.05f
			};

			_selectedFaceConfig.SnapPoints.Add(newPoint);
			_selectedPointIndex = _selectedFaceConfig.SnapPoints.Count - 1;
		}

		private void RemoveNearestPoint(Vector2 faceLocal)
		{
			if (_selectedFaceConfig?.SnapPoints == null || _selectedFaceConfig.SnapPoints.Count == 0) return;

			var closestIndex = -1;
			var minDistance = float.MaxValue;

			for (var i = 0; i < _selectedFaceConfig.SnapPoints.Count; i++)
			{
				var pointLocal = GetPointLocalPosition(_selectedFaceConfig.SnapPoints[i], _selectedFaceIndex);
				var distance = Vector2.Distance(faceLocal, pointLocal);
				if (distance < minDistance)
				{
					minDistance = distance;
					closestIndex = i;
				}
			}

			if (closestIndex != -1 && minDistance < 0.5f) // Within 0.5 units
			{
				_selectedFaceConfig.SnapPoints.RemoveAt(closestIndex);
				if (_selectedPointIndex >= closestIndex) _selectedPointIndex--;
			}
		}

		private Vector2 GetSnappedPosition(Vector2 localMousePos, Vector2 faceSize)
		{
			var snappedPos = localMousePos;
			var snapThreshold = 10f / (_gridSize * _2DZoom); // 화면 픽셀 기준 10px 이내일 때 스냅

			var halfSizeX = faceSize.x / 2f;
			var halfSizeY = faceSize.y / 2f;

			// X축 스냅 (왼쪽, 중앙, 오른쪽 가장자리)
			if (Mathf.Abs(snappedPos.x + halfSizeX) < snapThreshold) snappedPos.x = -halfSizeX;
			else if (Mathf.Abs(snappedPos.x) < snapThreshold) snappedPos.x = 0;
			else if (Mathf.Abs(snappedPos.x - halfSizeX) < snapThreshold) snappedPos.x = halfSizeX;

			// Y축 스냅 (아래, 중앙, 위 가장자리)
			if (Mathf.Abs(snappedPos.y + halfSizeY) < snapThreshold) snappedPos.y = -halfSizeY;
			else if (Mathf.Abs(snappedPos.y) < snapThreshold) snappedPos.y = 0;
			else if (Mathf.Abs(snappedPos.y - halfSizeY) < snapThreshold) snappedPos.y = halfSizeY;

			return snappedPos;
		}

		private string GetFaceName(int faceIndex)
		{
			return faceIndex switch
			{
				0 => "Top", 1 => "Bottom",
				2 => "Right", 3 => "Left",
				4 => "Front", 5 => "Back",
				_ => "Unknown"
			};
		}

		[TabGroup("MainTabs", "Step 2: Edit Snap Points")]
		[VerticalGroup("MainTabs/Step 2: Edit Snap Points/Bottom")]
		[Button("@GetSnapConfigButtonText()", ButtonSizes.Large)]
		[GUIColor(0.4f, 0.8f, 1f)]
		[ShowIf("CanGenerateSnapConfig")]
		[PropertySpace(SpaceBefore = 20)]
		private void GenerateAndAssignSnapConfig()
		{
			if (!CanGenerateSnapConfig()) return;

			var isUpdating = IsValidSnapConfig(targetData?.snapList);
			var originalPath = isUpdating ? AssetDatabase.GetAssetPath(targetData.snapList) : null;

			var generatedAssetPath = isUpdating
				? UpdateExistingSnapConfigList(_currentEditingData, snapPreviewTarget, originalPath)
				: GenerateSnapConfigList(_currentEditingData, snapPreviewTarget);

			if (string.IsNullOrEmpty(generatedAssetPath)) return;

			var newSnapList = AssetDatabase.LoadAssetAtPath<SnapConfigList>(generatedAssetPath);
			if (newSnapList == null || targetData == null) return;

			Undo.RecordObject(targetData, isUpdating ? "Update Snap Config List" : "Assign Snap Config List");
			targetData.snapList = newSnapList;
			EditorUtility.SetDirty(targetData);
			AssetDatabase.SaveAssets();

			EditorUtility.DisplayDialog("Success",
				isUpdating
					? $"Snap configuration updated for '{targetData.name}'\nSnap points: {newSnapList.snapPoints.Count}"
					: $"Snap configuration generated and assigned to '{targetData.name}'\nSnap points: {newSnapList.snapPoints.Count}",
				"OK");
		}

		private string GetSnapConfigButtonText()
		{
			return IsValidSnapConfig(targetData?.snapList)
				? "Update Existing Snap Config"
				: "Generate & Assign Snap Config";
		}

		private string UpdateExistingSnapConfigList(SnapEditingData editingData, GameObject target, string existingPath)
		{
			if (string.IsNullOrEmpty(existingPath))
				return GenerateSnapConfigList(editingData, target);

			var existingSnapList = AssetDatabase.LoadAssetAtPath<SnapConfigList>(existingPath);
			if (existingSnapList == null)
				return GenerateSnapConfigList(editingData, target);

			if (EditorUtility.DisplayDialog("Update Snap Config",
				    "Update existing snap configuration?\n\n" +
				    $"Current: {existingSnapList.snapPoints.Count} snap points\n" +
				    "This action can be undone.",
				    "Update", "Cancel"))
			{
				Undo.RecordObject(existingSnapList, "Update Snap Config List");
				var newSnapPoints = GenerateSnapPointsFromEditingData(editingData, target);
				existingSnapList.snapPoints = newSnapPoints;
				EditorUtility.SetDirty(existingSnapList);
				AssetDatabase.SaveAssets();
				Debug.Log($"Updated SnapConfigList: {existingSnapList.snapPoints.Count} snap points");
				return existingPath;
			}

			return null;
		}

		private List<SnapPointConfigData> GenerateSnapPointsFromEditingData(SnapEditingData editingData,
			GameObject target)
		{
			var allSnapPoints = new List<SnapPointConfigData>();

			for (var i = 0; i < 6; i++)
			{
				var faceConfig = editingData.GetFaceConfig(i);
				if (faceConfig.Generate && faceConfig.SnapPoints.Count > 0)
					allSnapPoints.AddRange(faceConfig.SnapPoints);
			}

			return allSnapPoints;
		}

		private string GenerateSnapConfigList(SnapEditingData editingData, GameObject target)
		{
			var allSnapPoints = GenerateSnapPointsFromEditingData(editingData, target);

			if (allSnapPoints.Count == 0)
			{
				// [수정됨] Debug.LogWarning 대신 EditorUtility.DisplayDialog 사용
				EditorUtility.DisplayDialog(
					"No snap points generated", // 대화상자 제목
					"No snap points generated.\n\nPlease check that you have added and activated a snap point on at least one face.\n", // 내용
					"Ok" // 버튼 텍스트
				);
				return null;
			}

			var snapList = CreateInstance<SnapConfigList>();
			snapList.snapPoints = allSnapPoints;

			EnsureDirectoryExists(SnapConfigSaveDir);
			var path = $"{SnapConfigSaveDir}/SnapConfig_{target.name.Replace("_Network", "")}.asset";
			var uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);

			AssetDatabase.CreateAsset(snapList, uniquePath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"Created SnapConfigList with {allSnapPoints.Count} snap points at: {uniquePath}", snapList);
			return uniquePath;
		}

		[TabGroup("MainTabs", "Step 2: Edit Snap Points")]
		[ShowIf("@this.targetData == null")]
		[InfoBox("Complete Step 1 first or assign Target Data to begin editing snap points", InfoMessageType.Warning)]
		private void ShowSnapNotAssignedWarning()
		{
		}

		#endregion

		// =============================================================================================================
		// STEP 3: REVIEW & DATABASE
		// =============================================================================================================

		#region Tab 3: Review & Edit Data

		[TabGroup("MainTabs", "Step 3: Review & Edit Data", Order = 3)]
		[HorizontalGroup("MainTabs/Step 3: Review & Edit Data/Split", 0.6f)]
		[ShowIf("@this.targetData != null && this.database != null")]
		[VerticalGroup("MainTabs/Step 3: Review & Edit Data/Split/Left")]
		[BoxGroup("MainTabs/Step 3: Review & Edit Data/Split/Left/Info")]
		[OnInspectorGUI]
		private void DrawDatabaseInfo()
		{
			if (database != null)
				EditorGUILayout.HelpBox(
					$"Database: {database.name}\n" +
					$"Current items: {database.placeableList.Count}\n" +
					$"Ready to add: {targetData?.name ?? "None"}",
					MessageType.Info);
		}

		[TabGroup("MainTabs", "Step 3: Review & Edit Data")]
		[HorizontalGroup("MainTabs/Step 3: Review & Edit Data/Split")]
		[VerticalGroup("MainTabs/Step 3: Review & Edit Data/Split/Left")]
		[BoxGroup("MainTabs/Step 3: Review & Edit Data/Split/Left/Database")]
		[Title("Database Management")]
		[LabelWidth(100)]
		public PlaceableDatabase database;

		[TabGroup("MainTabs", "Step 3: Review & Edit Data")]
		[VerticalGroup("MainTabs/Step 3: Review & Edit Data/Split/Left")]
		[BoxGroup("MainTabs/Step 3: Review & Edit Data/Split/Left/Database")]
		[Button("Find Database", ButtonSizes.Medium)]
		[PropertySpace(SpaceBefore = 5)]
		private void FindDatabase()
		{
			database = FindAndLoadFirstAsset<PlaceableDatabase>();
			if (database != null) Debug.Log($"Found database: {database.name}");
		}

		[TabGroup("MainTabs", "Step 3: Review & Edit Data")]
		[VerticalGroup("MainTabs/Step 3: Review & Edit Data/Split/Left")]
		[BoxGroup("MainTabs/Step 3: Review & Edit Data/Split/Left/Actions")]
		[Button("Add to Database", ButtonSizes.Large)]
		[GUIColor(0.4f, 1f, 0.4f)]
		[EnableIf("@this.targetData != null && this.database != null")]
		[PropertySpace(SpaceBefore = 20)]
		private void AddToDatabase()
		{
			if (targetData == null || database == null) return;

			var existingData = database?.placeableList?.FirstOrDefault(p => p?.itemName == targetData?.itemName);

			if (existingData != null)
			{
				var choice = EditorUtility.DisplayDialogComplex(
					"Duplicate Entry Found",
					$"An item with the name '{targetData.itemName}' already exists in the database.\n\n" +
					"What would you like to do?",
					"Replace Existing",
					"Cancel",
					"Create New (Auto-name)"
				);

				switch (choice)
				{
					case 0:
						ReplaceExistingData(existingData);
						break;
					case 1:
						Debug.Log("Add to database cancelled.");
						return;
					case 2:
						CreateNewWithAutoName();
						break;
				}
			}
			else
			{
				AddNewDataToDatabase();
			}
		}

		private void ReplaceExistingData(PlaceableData existingData)
		{
			if (EditorUtility.DisplayDialog(
				    "Confirm Replace",
				    $"Are you sure you want to replace '{existingData.itemName}' in the database?\n\n" +
				    "This action cannot be undone.",
				    "Yes, Replace",
				    "Cancel"))
			{
				Undo.RecordObject(database, "Replace PlaceableData in Database");
				var index = database.placeableList.IndexOf(existingData);
				if (index >= 0)
				{
					database.placeableList[index] = targetData;
					EditorUtility.SetDirty(database);
					AssetDatabase.SaveAssets();
					EditorUtility.DisplayDialog("Success",
						$"'{targetData.itemName}' has replaced the existing entry in the database.",
						"OK");
					ClearAllFields();
				}
			}
		}

		private void CreateNewWithAutoName()
		{
			var originalName = targetData.itemName;
			var originalId = targetData.id;
			var newName = GenerateUniqueName(originalName);

			Undo.RecordObject(targetData, "Auto-rename PlaceableData");
			targetData.itemName = newName;
			targetData.id = newName;

			AddNewDataToDatabase();

			if (EditorUtility.DisplayDialog(
				    "Keep New Name?",
				    $"The item was added as '{newName}'.\n\n" +
				    $"Would you like to keep this name or revert to '{originalName}'?",
				    "Keep New Name",
				    "Revert to Original"))
			{
				EditorUtility.SetDirty(targetData);
			}
			else
			{
				targetData.itemName = originalName;
				targetData.id = originalId;
				EditorUtility.SetDirty(targetData);
			}
		}

		private void AddNewDataToDatabase()
		{
			Undo.RecordObject(database, "Add PlaceableData to Database");
			database.placeableList.Add(targetData);
			EditorUtility.SetDirty(database);
			AssetDatabase.SaveAssets();

			EditorUtility.DisplayDialog("Success",
				$"'{targetData.itemName}' has been added to the database.\n" +
				$"Total items: {database.placeableList.Count}",
				"OK");

			if (EditorUtility.DisplayDialog("Clear Fields?",
				    "Would you like to start a new building?",
				    "Yes", "No"))
				ClearAllFields();
		}

		private string GenerateUniqueName(string baseName)
		{
			if (string.IsNullOrEmpty(baseName)) baseName = "NewBuilding";
			if (database?.placeableList == null) return baseName;

			var newName = baseName;
			var counter = 1;

			var match = Regex.Match(baseName, @"^(.+?)(\d+)$");
			if (match.Success)
			{
				baseName = match.Groups[1].Value.TrimEnd('_', ' ', '-');
				counter = int.Parse(match.Groups[2].Value) + 1;
			}

			while (database.placeableList.Any(p => p?.itemName == newName))
			{
				newName = $"{baseName}_{counter:00}";
				counter++;
			}

			return newName;
		}

		[TabGroup("MainTabs", "Step 3: Review & Edit Data")]
		[VerticalGroup("MainTabs/Step 3: Review & Edit Data/Split/Right")]
		[BoxGroup("MainTabs/Step 3: Review & Edit Data/Split/Right/DataReview")]
		[Title("Review Target Data")]
		[ShowIf("targetData")]
		[InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Hidden, Expanded = true)]
		public PlaceableData dataToEdit;

		[TabGroup("MainTabs", "Step 3: Review & Edit Data")]
		[ShowIf("@this.targetData == null")]
		[InfoBox("Complete Steps 1 & 2 or assign Target Data to review", InfoMessageType.Warning)]
		private void ShowDataNotAssignedWarning()
		{
		}

		#endregion

		#region Callbacks & Event Handlers

		private void OnTargetDataChanged()
		{
			dataToEdit = targetData;
			if (targetData != null)
			{
				_currentEditingData = new SnapEditingData();
				snapPreviewTarget = targetData.networkPrefab;

				if (targetData.snapList != null) LoadExistingSnapConfig(targetData.snapList);

				if (snapSettings == null) snapSettings = FindAndLoadFirstAsset<BuildingSystemSettings>();
				if (snapSettings != null) snapPresets = snapSettings.presets;
			}
			else
			{
				_currentEditingData = null;
				snapPreviewTarget = null;
				snapPresets = null;
			}

			_selectedFaceIndex = -1;
			_selectedFaceConfig = null;
			RequestRepaint();
		}

		private void RequestRepaint()
		{
			if (!_needsRepaint)
			{
				_needsRepaint = true;
				EditorApplication.delayCall += () =>
				{
					if (_needsRepaint)
					{
						_needsRepaint = false;
						Repaint();
					}
				};
			}
		}

		private void LoadExistingSnapConfig(SnapConfigList existingSnapList)
		{
			if (existingSnapList == null || _currentEditingData == null) return;

			if (existingSnapList.snapPoints == null || existingSnapList.snapPoints.Count == 0)
			{
				Debug.LogWarning("Existing snap config has no snap points");
				return;
			}

			_currentEditingData.ClearAll();

			if (!TryGetBounds(snapPreviewTarget, out var localCenter, out var localExtents))
			{
				Debug.LogWarning("Could not calculate bounds for loading snap config");
				return;
			}

			foreach (var snapPoint in existingSnapList.snapPoints)
			{
				var faceIndex = DetermineFaceFromPosition(snapPoint.localPosition, localCenter, localExtents);
				if (faceIndex < 0) continue;

				var faceConfig = _currentEditingData.GetFaceConfig(faceIndex);
				faceConfig.Generate = true;
				faceConfig.SnapPoints.Add(snapPoint);
			}

			Debug.Log($"Loaded {existingSnapList.snapPoints.Count} snap points from existing configuration");
		}

		private int DetermineFaceFromPosition(Vector3 localPos, Vector3 center, Vector3 extents)
		{
			var distances = new float[6];
			distances[0] = Vector3.Distance(localPos, center + Vector3.up * extents.y);
			distances[1] = Vector3.Distance(localPos, center + Vector3.down * extents.y);
			distances[2] = Vector3.Distance(localPos, center + Vector3.right * extents.x);
			distances[3] = Vector3.Distance(localPos, center + Vector3.left * extents.x);
			distances[4] = Vector3.Distance(localPos, center + Vector3.forward * extents.z);
			distances[5] = Vector3.Distance(localPos, center + Vector3.back * extents.z);

			var closestFace = 0;
			var minDistance = distances[0];

			for (var i = 1; i < 6; i++)
				if (distances[i] < minDistance)
				{
					minDistance = distances[i];
					closestFace = i;
				}

			return closestFace;
		}

		private void OnBaseModelChanged()
		{
			if (genBaseModel != null) genItemName = genBaseModel.name;
		}

		private void ApplyGeneratorPreset()
		{
			if (genPreset != null)
			{
				genPlacementMask = genPreset.placementMask;
				genCollisionMask = genPreset.collisionMask;
				genPreviewObjectLayer = genPreset.previewLayer;
				genNetworkObjectLayer = genPreset.networkLayer;
				genBuildingPartType = genPreset.buildingPartType;
				genPreviewGreenMaterial = genPreset.previewGreenMaterial;
				genPreviewRedMaterial = genPreset.previewRedMaterial;
				genPrefabSavePath = genPreset.prefabSavePath;
				genDataSavePath = genPreset.dataSavePath;
			}
			else
			{
				genPreviewObjectLayer = "Preview";
				genNetworkObjectLayer = "Building";
				genBuildingPartType = BuildingPartType.Floor;
				genPlacementMask = LayerMask.GetMask("Ground", "Building");
				genCollisionMask = LayerMask.GetMask("Water", "Ground", "Building", "Obstacle");
				genPrefabSavePath = DefaultPrefabSavePath;
				genDataSavePath = DefaultDataSavePath;
				FindDefaultMaterials();
			}
		}

		#endregion

		#region Core Logic: Asset Generation

		private void GenerateInitialAssets(string dataAssetPath)
		{
			EnsureDirectoryExists(genPrefabSavePath);
			EnsureDirectoryExists(Path.GetDirectoryName(dataAssetPath));

			var previewPrefab = CreatePrefabVariant("_Preview", genPreviewObjectLayer, true);
			var networkPrefab = CreatePrefabVariant("_Network", genNetworkObjectLayer, false);

			CreateOrUpdatePlaceableData(dataAssetPath, previewPrefab, networkPrefab);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		// BuildingMasterEditorWindow.cs
		// BuildingMasterEditorWindow.cs

private const string VisualsChildName = "Visuals";

private GameObject CreatePrefabVariant(string suffix, string layerName, bool isPreview)
{
    if (genBaseModel == null)
    {
        Debug.LogError("Base model is not assigned");
        return null;
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(genBaseModel);
    if (instance == null)
    {
        Debug.LogError("Failed to instantiate prefab from base model");
        return null;
    }

    instance.name = $"{genItemName}{suffix}";
    instance.layer = LayerMask.NameToLayer(layerName);

    var visualTransform = SetupVisualsHierarchy(instance);
    SetupLayersRecursively(instance);
    SetupColliderOnPrefab(instance, isPreview);
    SetupRigidbody(instance);
    SetupBuildingComponents(instance, isPreview, visualTransform);

    return SavePrefabAsset(instance);
}

private Transform SetupVisualsHierarchy(GameObject instance)
{
    if (instance == null) return null;

    var rootRenderer = instance.GetComponent<Renderer>();
    var rootMeshFilter = instance.GetComponent<MeshFilter>();
    
    // Case 1: 이미 Visuals 자식이 있는 경우
    var existingVisuals = instance.transform.Find(VisualsChildName);
    if (existingVisuals != null)
    {
        Debug.Log($"[{instance.name}] 기존 '{VisualsChildName}' 자식을 사용합니다.");
        return existingVisuals;
    }
    
    // Case 2: 루트에 렌더러가 있는 경우 -> Visuals 자식으로 이동
    if (rootRenderer != null && rootMeshFilter != null)
    {
        return MoveRenderersToVisualsChild(instance, rootRenderer, rootMeshFilter);
    }
    
    // Case 3: 자식에만 렌더러가 있는 경우
    return FindAndRenameVisualsChild(instance);
}

private Transform MoveRenderersToVisualsChild(GameObject instance, Renderer rootRenderer, MeshFilter rootMeshFilter)
{
    Debug.Log($"[{instance.name}] 루트 렌더러를 '{VisualsChildName}' 자식으로 이동합니다.");
    
    var visualChild = new GameObject(VisualsChildName);
    visualChild.transform.SetParent(instance.transform, false);
    
    // MeshFilter 복사
    var childFilter = visualChild.AddComponent<MeshFilter>();
    childFilter.sharedMesh = rootMeshFilter.sharedMesh;
    
    // MeshRenderer 복사
    var childRenderer = visualChild.AddComponent<MeshRenderer>();
    CopyRendererProperties(rootRenderer, childRenderer);
    
    // 원본 컴포넌트 제거
    DestroyImmediate(rootRenderer);
    DestroyImmediate(rootMeshFilter);
    
    return visualChild.transform;
}

private void CopyRendererProperties(Renderer source, Renderer target)
{
    if (source == null || target == null) return;

    target.sharedMaterials = source.sharedMaterials;
    target.shadowCastingMode = source.shadowCastingMode;
    target.receiveShadows = source.receiveShadows;
    target.lightProbeUsage = source.lightProbeUsage;
    target.reflectionProbeUsage = source.reflectionProbeUsage;
}

private Transform FindAndRenameVisualsChild(GameObject instance)
{
    var childRenderers = instance.GetComponentsInChildren<Renderer>(true);
    if (childRenderers.Length > 0)
    {
        var visualTransform = childRenderers[0].transform;
        Debug.Log($"[{instance.name}] 자식 '{visualTransform.name}'을 {VisualsChildName}로 사용합니다.");
        
        if (visualTransform.name != VisualsChildName)
        {
            visualTransform.name = VisualsChildName;
        }
        return visualTransform;
    }
    
    Debug.LogWarning($"[{instance.name}] 렌더러를 찾을 수 없습니다!");
    return null;
}

private void SetupLayersRecursively(GameObject instance)
{
    if (instance == null) return;

    foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
    {
        child.gameObject.layer = instance.layer;
    }
}

private void SetupRigidbody(GameObject instance)
{
    if (instance == null) return;

    if (!instance.TryGetComponent<Rigidbody>(out var rb)) 
        rb = instance.AddComponent<Rigidbody>();
    rb.useGravity = false;
    rb.isKinematic = true;
}

private void SetupBuildingComponents(GameObject instance, bool isPreview, Transform visualTransform)
{
    if (instance == null) return;

    if (isPreview)
    {
        SetupPreviewComponents(instance, visualTransform);
    }
    else
    {
        SetupNetworkComponents(instance);
    }
}

private void SetupPreviewComponents(GameObject instance, Transform visualTransform)
{
    var previewScript = instance.AddComponent<PreviewBuilding>();
    previewScript.green = genPreviewGreenMaterial;
    previewScript.red = genPreviewRedMaterial;
    
    if (visualTransform != null)
    {
        var visualsField = typeof(PreviewBuilding).GetField("visuals", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        visualsField?.SetValue(previewScript, visualTransform);
    }
}

private void SetupNetworkComponents(GameObject instance)
{
    if (instance.GetComponent<NetworkObject>() == null) 
        instance.AddComponent<NetworkObject>();
    if (instance.GetComponent<NetworkBuilding>() == null) 
        instance.AddComponent<NetworkBuilding>();
    if (instance.GetComponent<NetworkTransform>() == null) 
        instance.AddComponent<NetworkTransform>();
}

private GameObject SavePrefabAsset(GameObject instance)
{
    if (instance == null) return null;

    var prefabPath = Path.Combine(genPrefabSavePath, $"{instance.name}.prefab");
    var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
    DestroyImmediate(instance);
    
    return prefab;
}

		private void SetupColliderOnPrefab(GameObject instance, bool isTrigger)
		{
			var collider = instance.GetComponentInChildren<Collider>();
			if (collider == null)
			{
				switch (defaultColliderType)
				{
					case DefaultColliderType.Box:
						collider = instance.AddComponent<BoxCollider>();
						collider.isTrigger = isTrigger;
						break;
					case DefaultColliderType.Mesh:
						var mc = instance.AddComponent<MeshCollider>();
						mc.convex = false;
						collider = mc;
						break;
				}

				Debug.LogWarning(
					$"No existing collider found on '{instance.name}'. Added a '{defaultColliderType}' collider.",
					instance);
			}
		}

		private void CreateOrUpdatePlaceableData(string path, GameObject previewPrefab, GameObject networkPrefab)
		{
			var data = AssetDatabase.LoadAssetAtPath<PlaceableData>(path);
			var isNewAsset = data == null;
			if (isNewAsset) data = CreateInstance<PlaceableData>();

			Undo.RecordObject(data, "Update Placeable Data");

			data.itemName = genItemName;
			data.id = genItemName;
			data.previewPrefab = previewPrefab;
			data.networkPrefab = networkPrefab;
			data.placementMask = genPlacementMask;
			data.collisionMask = genCollisionMask;

			if (isNewAsset) AssetDatabase.CreateAsset(data, path);

			EditorUtility.SetDirty(data);
			Selection.activeObject = data;
		}

		#endregion

		#region Helper Methods: Geometry & Math

		private static bool TryGetBounds(GameObject target, out Vector3 localCenter, out Vector3 localExtents)
		{
			localCenter = Vector3.zero;
			localExtents = Vector3.one * 0.5f;
			if (target == null) return false;

			var colliders = target.GetComponents<Collider>();

			if (colliders.Length > 0)
			{
				Bounds? combinedBounds = null;

				foreach (var collider in colliders)
				{
					Bounds currentBounds;

					switch (collider)
					{
						case BoxCollider box:
							currentBounds = new Bounds(box.center, box.size);
							break;

						case SphereCollider sphere:
							var diameter = sphere.radius * 2;
							currentBounds = new Bounds(sphere.center, Vector3.one * diameter);
							break;

						case CapsuleCollider capsule:
							var size = new Vector3(capsule.radius * 2, capsule.height, capsule.radius * 2);
							if (capsule.direction == 0)
								size = new Vector3(capsule.height, capsule.radius * 2, capsule.radius * 2);
							else if (capsule.direction == 2)
								size = new Vector3(capsule.radius * 2, capsule.radius * 2, capsule.height);
							currentBounds = new Bounds(capsule.center, size);
							break;

						default:
							currentBounds = collider.bounds;
							currentBounds.center = target.transform.InverseTransformPoint(currentBounds.center);
							currentBounds.size = target.transform.InverseTransformVector(currentBounds.size);
							break;
					}

					if (!combinedBounds.HasValue)
						combinedBounds = currentBounds;
					else
						combinedBounds.Value.Encapsulate(currentBounds);
				}

				if (combinedBounds.HasValue)
				{
					localCenter = combinedBounds.Value.center;
					localExtents = combinedBounds.Value.extents;
					return true;
				}
			}

			var renderers = target.GetComponentsInChildren<Renderer>();

			if (renderers.Length > 0)
			{
				Debug.LogWarning(
					$"No colliders found on '{target.name}'. Using renderer bounds as a fallback.",
					target);
				var bounds = renderers[0].bounds;
				for (var i = 1; i < renderers.Length; i++)
					bounds.Encapsulate(renderers[i].bounds);

				localCenter = target.transform.InverseTransformPoint(bounds.center);
				localExtents = target.transform.InverseTransformVector(bounds.extents);
				localExtents = new Vector3(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y),
					Mathf.Abs(localExtents.z));

				return true;
			}

			Debug.LogError($"Could not determine bounds for '{target.name}'. It has no colliders or renderers.",
				target);
			return false;
		}

		private Vector2? WorldToScreenPoint(Rect rect, Vector3 worldPos)
		{
			var screenPos = _previewRenderUtility.camera.WorldToViewportPoint(worldPos);
			if (screenPos.z < 0) return null;
			return new Vector2(rect.x + screenPos.x * rect.width, rect.y + (1 - screenPos.y) * rect.height);
		}

		private Vector3 GetFaceNormal(int faceIndex)
		{
			return faceIndex switch
			{
				0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.right,
				3 => Vector3.left, 4 => Vector3.forward, 5 => Vector3.back,
				_ => Vector3.up
			};
		}

		private Vector2 GetFaceSize(int faceIndex, Vector3 extents)
		{
			return faceIndex switch
			{
				0 or 1 => new Vector2(extents.x * 2, extents.z * 2),
				2 or 3 => new Vector2(extents.z * 2, extents.y * 2),
				4 or 5 => new Vector2(extents.x * 2, extents.y * 2),
				_ => Vector2.one
			};
		}

		#endregion

		#region Helper Methods: Preview Rendering

		private void SetupPreviewUtility()
		{
			if (_previewRenderUtility != null) return;

			_previewRenderUtility = new PreviewRenderUtility
			{
				camera =
				{
					fieldOfView = 60.0f,
					farClipPlane = 100,
					nearClipPlane = 0.01f
				}
			};

			_previewRenderUtility.lights[0].intensity = 1.4f;
			_previewRenderUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
			_previewRenderUtility.lights[1].intensity = 0.4f;
			_previewRenderUtility.ambientColor = new Color(0.1f, 0.1f, 0.1f, 0);
		}

		private void RenderPreview(Rect rect)
		{
			if (_previewRenderUtility?.camera == null) return;

		_previewRenderUtility.BeginPreview(rect, GUI.skin.box);

			var distance = 4.0f;
			if (snapPreviewTarget != null)
				if (TryGetBounds(snapPreviewTarget, out _, out var extents))
				{
					var maxExtent = Mathf.Max(extents.x, extents.y, extents.z);
					distance = maxExtent * 4.0f;
					distance = Mathf.Clamp(distance, 2.0f, 20.0f);
				}

			var camRotation = Quaternion.Euler(_previewDir.y, _previewDir.x, 0);
			var camPosition = camRotation * (Vector3.forward * -distance * _previewZoom);
			camPosition += camRotation * new Vector3(_previewPan.x, _previewPan.y, 0);

			_previewRenderUtility.camera.transform.SetPositionAndRotation(camPosition, camRotation);

			if (snapPreviewTarget != null)
			{
				var wasActive = snapPreviewTarget.activeSelf;
				snapPreviewTarget.SetActive(true);

				foreach (var renderer in snapPreviewTarget.GetComponentsInChildren<Renderer>())
					if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var mf) && mf?.sharedMesh != null)
						for (var i = 0; i < mf.sharedMesh.subMeshCount; i++)
						{
							if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0) continue;
							var material = renderer.sharedMaterials[i % renderer.sharedMaterials.Length];
							_previewRenderUtility.DrawMesh(mf.sharedMesh, renderer.transform.localToWorldMatrix,
								material, i);
						}

				snapPreviewTarget.SetActive(wasActive);
			}

			_previewRenderUtility.camera.Render();
			var result = _previewRenderUtility.EndPreview();
			GUI.DrawTexture(rect, result);
		}

		#endregion

		#region Helper Methods: Asset & Utility

		private void ClearAllFields()
		{
			targetData = null;
			genPreset = null;
			genBaseModel = null;
			genItemName = "NewBuilding";
			database = null;
			ApplyGeneratorPreset();
			OnTargetDataChanged();
			Debug.Log("Editor fields have been cleared.");
		}

		private static Color GetColorForSnapType(SnapType type)
		{
			return type switch
			{
				SnapType.Floor => Color.green,
				SnapType.Wall => Color.blue,
				SnapType.Ceiling => Color.cyan,
				SnapType.Foundation => new Color(0.5f, 0.2f, 0.1f),
				SnapType.Roof => Color.red,
				SnapType.Socket => Color.yellow,
				SnapType.Pillar => Color.magenta,
				_ => Color.white
			};
		}

		private void FindDefaultMaterials()
		{
			if (genPreviewGreenMaterial == null) genPreviewGreenMaterial = FindMaterialByName("Preview Green");
			if (genPreviewRedMaterial == null) genPreviewRedMaterial = FindMaterialByName("Preview Red");
		}

		private static Material FindMaterialByName(string materialName)
		{
			var guids = AssetDatabase.FindAssets($"t:Material {materialName}");
			if (guids.Length > 0)
				return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));

			Debug.LogWarning($"Could not find default material '{materialName}' in the project.");
			return null;
		}

		private static T FindAndLoadFirstAsset<T>() where T : ScriptableObject
		{
			var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
			if (guids.Length > 0) return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));

			Debug.LogWarning($"Could not find any asset of type {typeof(T).Name} in the project.");
			return null;
		}

		private static void EnsureDirectoryExists(string path)
		{
			if (!string.IsNullOrEmpty(path) && !Directory.Exists(path)) Directory.CreateDirectory(path);
		}

		private bool ShowSnapPreview()
		{
			return _currentEditingData != null && snapPreviewTarget != null;
		}

		private bool CanGenerateSnapConfig()
		{
			return _currentEditingData != null && snapPreviewTarget != null;
		}

		private bool ShouldShowDefaultColliderType()
		{
			return genBaseModel != null && genBaseModel.GetComponentInChildren<Collider>() == null;
		}

		private static bool IsValidSnapConfig(SnapConfigList snapList)
		{
			return snapList != null && snapList.snapPoints is { Count: > 0 };
		}

		#endregion
	}
}