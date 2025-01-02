using System.Linq;
using UnityEditor;
using UnityEngine;
using static CollisionBear.OpenLevelDraft.LevelSystem;

namespace CollisionBear.OpenLevelDraft
{
    [CustomEditor(typeof(LevelSystem))]
    public class LevelSystemEditor : Editor
    {
        [MenuItem("GameObject/3D Object/Level #P")]
        public static void CreateRiverSystem()
        {
            var levelGameObject = new GameObject("Level System");
            levelGameObject.transform.position = GetMiddleOfViewPort();
            var levelSystem = levelGameObject.AddComponent<LevelSystem>();
            levelSystem.Material = Resources.Load<Material>("Prototype2Units");
            levelSystem.Tool = LevelSystem.SplineToolType.Edit;

            Selection.activeGameObject = levelGameObject;
        }

        private static Vector3 GetMiddleOfViewPort()
        {
            var middleOfViewRay = SceneView.lastActiveSceneView.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 1));
            if (Physics.Raycast(middleOfViewRay, out RaycastHit rayCasthit)) {

                var result =  rayCasthit.point;
                result.y = 0;
                return result;
            } else {
                return new Vector3(0, 0, 0);
            }
        }

        public class InWorldPosition
        {
            public Vector3 Position;
            public bool IsInWorld;
            public bool IsInSystem;
            public LevelSystem.ControlPointPair ControlPoints;
        }

        private GUIStyle EditorTextStyle;

        public override void OnInspectorGUI()
        {
            var levelSystem = target as LevelSystem;

            using (var scope = new EditorGUI.ChangeCheckScope()) {

                levelSystem.Width = Mathf.Clamp(EditorGUILayout.FloatField("Width", levelSystem.Width), 0, 10);
                levelSystem.Height = Mathf.Clamp(EditorGUILayout.FloatField("Height", levelSystem.Height), 0, 10);

                EditorGUILayout.Space();

                levelSystem.Material = EditorGUILayout.ObjectField("Material", levelSystem.Material, typeof(Material), false) as Material;
                levelSystem.UvScale = EditorGUILayout.FloatField("UV Scale", levelSystem.UvScale);
                levelSystem.SmoothingLevel = Mathf.Clamp(EditorGUILayout.IntField("Smoothing Level", levelSystem.SmoothingLevel), 0, 10);

                EditorGUILayout.Space();

                if (levelSystem.SplineCapMode == LevelSystem.SplineCapModeType.Open) {
                    if (GUILayout.Button("Close spline")) {
                        levelSystem.SplineCapMode = LevelSystem.SplineCapModeType.Closed;
                        levelSystem.UpdateMesh();
                        return;
                    }
                } else if (levelSystem.SplineCapMode == LevelSystem.SplineCapModeType.Closed) {
                    if (GUILayout.Button("Open spline")) {
                        levelSystem.SplineCapMode = LevelSystem.SplineCapModeType.Open;
                        levelSystem.UpdateMesh();
                        return;
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledGroupScope(levelSystem.Tool == LevelSystem.SplineToolType.Edit)) {
                        if (GUILayout.Button("Edit path\t(E)", GUILayout.Height(24))) {
                            levelSystem.Tool = LevelSystem.SplineToolType.Edit;
                        }
                    }

                    using (new EditorGUI.DisabledGroupScope(levelSystem.Tool == LevelSystem.SplineToolType.Split)) {
                        if (GUILayout.Button("Split path\t(R)", GUILayout.Height(24))) {
                            levelSystem.Tool = LevelSystem.SplineToolType.Split;
                        }
                    }

                    using (new EditorGUI.DisabledGroupScope(levelSystem.Tool == LevelSystem.SplineToolType.None)) {
                        if (GUILayout.Button("Cancel\t(Escape)", GUILayout.Height(24))) {
                            levelSystem.Tool = LevelSystem.SplineToolType.None;
                        }
                    }
                }

                if (scope.changed) {
                    Undo.RecordObject(levelSystem, "Updated Level System");
                    levelSystem.UpdateMesh();
                }
            }
        }

        public void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneView;
        }

        public void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneView;

            var river = target as LevelSystem;

            if (river == null) {
                return;
            }
        }

        public void OnSceneView(SceneView sceneView)
        {
            EditorTextStyle = new GUIStyle() {
                normal = new GUIStyleState {
                    textColor = Color.white,
                },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            var levelSystem = target as LevelSystem;
            if(levelSystem == null) {
                return;
            }

            var currentEvent = Event.current;

            if (levelSystem.Tool == LevelSystem.SplineToolType.None) {
                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.E) {
                    levelSystem.Tool = LevelSystem.SplineToolType.Edit;
                    currentEvent.Use();
                }

                return;
            }

            if (levelSystem.Tool == LevelSystem.SplineToolType.Edit) {
                HandleEditMode(levelSystem, sceneView, currentEvent);
            }
        }

        private void HandleEditMode(LevelSystem levelSystem, SceneView sceneView, Event currentEvent) {

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            foreach (var point in levelSystem.ControlPoints) {
                ShowControlPoint(point, levelSystem);
            }

            DrawCurvedLine(levelSystem);

            var inWorldPosition = GetInWorldPoint(currentEvent.mousePosition, levelSystem);

            if (currentEvent.control) {
                var lastPoint = levelSystem.ControlPoints.Last();
                var lastPointPosition = levelSystem.transform.position + lastPoint.Position;

                if (inWorldPosition.IsInSystem) {
                    if (currentEvent.type == EventType.MouseDown) {
                        if (inWorldPosition.ControlPoints != null) {
                            Undo.RecordObject(levelSystem, "Inserted control point");
                            levelSystem.InsertControlPoint(inWorldPosition.ControlPoints, inWorldPosition.Position);
                        }
                        currentEvent.Use();
                    }
                } else {
                    Handles.DrawLine(lastPointPosition, inWorldPosition.Position);

                    if (currentEvent.type == EventType.MouseDown) {
                        Undo.RecordObject(levelSystem, "Added additional control point");
                        levelSystem.AddControlPoint(inWorldPosition.Position);
                        currentEvent.Use();
                    }
                }
            } else if (currentEvent.shift) {
                if (currentEvent.type == EventType.MouseDown) {
                    if (inWorldPosition.ControlPoints != null) {
                        Undo.RecordObject(levelSystem, "Removed control point");
                        levelSystem.RemoveControlPoint(inWorldPosition.ControlPoints.First);
                    }
                    currentEvent.Use();
                }

            } else {
                Handles.Label(inWorldPosition.Position + Vector3.down * 2, "Hold control to place point\nHold shift to remove a point\nPress space to release", EditorTextStyle);
            }

            if (levelSystem.Tool == LevelSystem.SplineToolType.Edit) {
                if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.Space || currentEvent.keyCode == KeyCode.Escape)) {
                    levelSystem.Tool = LevelSystem.SplineToolType.None;
                    currentEvent.Use();
                }
            }

            sceneView.Repaint();
        }

        private void DrawCurvedLine(LevelSystem levelSystem)
        {
            foreach (var pair in levelSystem.GetControlPointPairs(levelSystem.ControlPoints)) {
                ShowBezierSegment(levelSystem, pair);
            }

            if (levelSystem.SplineCapMode == LevelSystem.SplineCapModeType.Closed) {
                ShowBezierSegment(levelSystem, new ControlPointPair(levelSystem.ControlPoints.Last(), levelSystem.ControlPoints.First()));
            }
        }

        private void ShowBezierSegment(LevelSystem levelSystem, ControlPointPair pair) {
            var distance = (pair.Second.Position - pair.First.Position).magnitude / 3;
            var firstPoint = pair.First.Position + levelSystem.transform.position;
            var lastPoint = pair.Second.Position + levelSystem.transform.position;
            var extraPosition01 = pair.First.Position + pair.First.Direction * Vector3.forward * distance + levelSystem.transform.position;
            var extraPosition02 = pair.Second.Position + pair.Second.Direction * Vector3.back * distance + levelSystem.transform.position;

            Handles.DrawBezier(firstPoint, lastPoint, extraPosition01, extraPosition02, Color.green, null, 2);
        }

        private void ShowControlPoint(LevelSystem.RiverControlPoint controlPoint, LevelSystem river)
        {
            var position = river.transform.position + controlPoint.Position;

            using (var scope = new EditorGUI.ChangeCheckScope()) {
                controlPoint.Position = Handles.DoPositionHandle(position, controlPoint.Direction) - river.transform.position;
                controlPoint.Position.y = 0;
                controlPoint.Direction = Handles.Disc(controlPoint.Direction, position, Vector3.up, 3, false, 0);

                if (scope.changed) {
                    Undo.RecordObject(river, "Edited River control point");
                    river.UpdateMesh();
                    EditorUtility.SetDirty(river);
                }
            }
        }

        private InWorldPosition GetInWorldPoint(Vector2 position, LevelSystem river)
        {
            if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(position), out RaycastHit raycastHit, float.MaxValue, int.MaxValue, QueryTriggerInteraction.Ignore)) {
                if (raycastHit.collider.gameObject == river.gameObject) {
                    return new InWorldPosition { Position = raycastHit.point, IsInWorld = true, IsInSystem = true, ControlPoints = GetSelectedControlPoint(raycastHit, river) };
                } else {
                    return new InWorldPosition { Position = raycastHit.point, IsInWorld = true, IsInSystem = false };
                }
            } else {
                return new InWorldPosition { Position = Vector3.zero, IsInWorld = false };
            }
        }

        private LevelSystem.ControlPointPair GetSelectedControlPoint(RaycastHit raycastHit, LevelSystem levelSystem)
        {
            var triangleIndex = raycastHit.triangleIndex;
            if (levelSystem.SplineCapMode == LevelSystem.SplineCapModeType.Open) {
                triangleIndex -= 4;
            }

            var segmentIndex = triangleIndex / 6;
            var placedSegmentIndex = Mathf.Clamp((segmentIndex + 1) / (levelSystem.SmoothingLevel + 1), 0, int.MaxValue);
            var nextSegmentIndex = Mathf.Clamp(segmentIndex / (levelSystem.SmoothingLevel + 1), 0, int.MaxValue);

            if (nextSegmentIndex == placedSegmentIndex) {
                nextSegmentIndex = placedSegmentIndex + 1;
            }

            if(placedSegmentIndex < 0 || nextSegmentIndex >= levelSystem.ControlPoints.Count) {
                return null;
            }

            return new LevelSystem.ControlPointPair(levelSystem.ControlPoints[placedSegmentIndex], levelSystem.ControlPoints[nextSegmentIndex]);
        }
    }
}
