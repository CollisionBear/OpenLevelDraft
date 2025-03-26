using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static CollisionBear.OpenLevelDraft.LevelSystem;

namespace CollisionBear.OpenLevelDraft
{
    [CustomEditor(typeof(LevelSystem))]
    public class LevelSystemEditor : Editor {
        private const float SplitGap = 0.5f;

        [MenuItem("GameObject/3D Object/Level #P")]
        public static void CreateRiverSystem()
        {
            var position = GetMiddleOfViewPort();
            var levelSystem = CreateLevelSystem(position);
            levelSystem.Tool = LevelSystem.SplineToolType.Edit;

            Selection.activeGameObject = levelSystem.gameObject;
        }

        private static LevelSystem CreateLevelSystem(Vector3 position, Material material = null) {
            if(material == null) {
                material = Resources.Load<Material>("Prototype2Units");
            }

            var levelGameObject = new GameObject("Level System");

            levelGameObject.transform.position = position;
            var meshRenderer = levelGameObject.AddComponent<MeshRenderer>();
            meshRenderer.material = material;
            var result = levelGameObject.AddComponent<LevelSystem>();
            return result;
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
            public float PositionFactor;
        }

        private GUIStyle EditorTextStyle;

        public override void OnInspectorGUI()
        {
            var levelSystem = target as LevelSystem;

            using (var scope = new EditorGUI.ChangeCheckScope()) {

                levelSystem.Width = Mathf.Clamp(EditorGUILayout.FloatField("Width", levelSystem.Width), 0, 10);
                levelSystem.Height = Mathf.Clamp(EditorGUILayout.FloatField("Height", levelSystem.Height), 0, 10);

                EditorGUILayout.Space();

                levelSystem.UvScale = EditorGUILayout.FloatField("UV Scale", levelSystem.UvScale);
                levelSystem.SmoothingLevel = Mathf.Clamp(EditorGUILayout.IntField("Smoothing Level", levelSystem.SmoothingLevel), 0, 10);

                EditorGUILayout.Space();

                levelSystem.Options = (LevelSystem.ControlPointOptions)EditorGUILayout.EnumFlagsField("Options", levelSystem.Options);

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
                        if (GUILayout.Button("Split path", GUILayout.Height(24))) {
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

            HandleTools(levelSystem, sceneView, currentEvent);
        }

        private void HandleTools(LevelSystem levelSystem, SceneView sceneView, Event currentEvent) {

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            
            if(levelSystem.Tool == SplineToolType.Edit) {
                foreach (var point in levelSystem.ControlPoints) {
                    ShowControlPoint(point, levelSystem);
                }
            }

            var inWorldPosition = GetInWorldPoint(currentEvent.mousePosition, levelSystem);

            if (currentEvent.control) {
                var lastPoint = levelSystem.ControlPoints.Last();
                var lastPointPosition = levelSystem.transform.position + lastPoint.Position;

                if (inWorldPosition.IsInSystem) {
                    if (currentEvent.type == EventType.MouseDown) {
                        if (levelSystem.Tool == SplineToolType.Edit) {
                            HandleEditInsert(levelSystem, inWorldPosition);
                        } else if (levelSystem.Tool == SplineToolType.Split) {
                            HandleSplit(levelSystem, inWorldPosition);
                        }

                        currentEvent.Use();
                    }
                } else {
                    Handles.DrawLine(lastPointPosition, inWorldPosition.Position);

                    if (currentEvent.type == EventType.MouseDown) {
                        if (levelSystem.Tool == SplineToolType.Edit) {
                            HandleEditAdd(levelSystem, inWorldPosition);
                        }

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

        private void HandleEditInsert(LevelSystem levelSystem, InWorldPosition inWorldPosition) {
            if (inWorldPosition.ControlPoints == null) {
                return;
            }

            Undo.RecordObject(levelSystem, "Inserted control point");
            levelSystem.InsertControlPoint(inWorldPosition.ControlPoints, inWorldPosition.Position);
        }

        private void HandleSplit(LevelSystem levelSystem, InWorldPosition inWorldPosition) {
            if (inWorldPosition.ControlPoints == null) {
                return;
            }

            if(levelSystem.SplineCapMode == SplineCapModeType.Closed) {
                SplitClosedLoop(levelSystem, inWorldPosition);
            } else if (levelSystem.SplineCapMode == SplineCapModeType.Open) {
                SplitOpenLoop(levelSystem, inWorldPosition);
            }

            levelSystem.Tool = SplineToolType.Edit;
        }

        private void SplitClosedLoop(LevelSystem levelSystem, InWorldPosition inWorldPosition) {
            var position = BezierCurves.CubicCurve(inWorldPosition.ControlPoints.GetBezierValues(), 0.5f);
            var direction = inWorldPosition.ControlPoints.Direction();

            // We're trying to split between the last and first point
            if (inWorldPosition.ControlPoints.First == levelSystem.ControlPoints.Last()) {
                levelSystem.ControlPoints.Insert(0, new RiverControlPoint { Position = position + direction * SplitGap, Direction = Quaternion.LookRotation(direction) });
                levelSystem.ControlPoints.Add(new RiverControlPoint { Position = position - direction * SplitGap, Direction = Quaternion.LookRotation(direction) });

                levelSystem.SplineCapMode = SplineCapModeType.Open;
                levelSystem.UpdateMesh();
                return;
            }

            var newControlPoints = new List<RiverControlPoint>();

            var firstLevelSystemPoints = GetControlPointsBetween(levelSystem, levelSystem.ControlPoints.First(), inWorldPosition.ControlPoints.First);
            var secondLevelSystemPoints = GetControlPointsBetween(levelSystem, inWorldPosition.ControlPoints.Second, levelSystem.ControlPoints.Last());

            newControlPoints.AddRange(secondLevelSystemPoints);
            newControlPoints.AddRange(firstLevelSystemPoints);

            newControlPoints.Insert(0, new RiverControlPoint { Position = position + direction * SplitGap, Direction = Quaternion.LookRotation(direction) });
            newControlPoints.Add(new RiverControlPoint { Position = position - direction * SplitGap, Direction = Quaternion.LookRotation(direction) });

            levelSystem.SplineCapMode = SplineCapModeType.Open;
            levelSystem.ControlPoints = newControlPoints;
            levelSystem.UpdateMesh();
        }

        private void SplitOpenLoop(LevelSystem levelSystem, InWorldPosition inWorldPosition) {
            inWorldPosition.Position.y = inWorldPosition.ControlPoints.First.Position.y;

            var firstLevelSystemPoints = GetControlPointsBetween(levelSystem, levelSystem.ControlPoints.First(), inWorldPosition.ControlPoints.First);
            var secondLevelSystemPoints = GetControlPointsBetween(levelSystem, inWorldPosition.ControlPoints.Second, levelSystem.ControlPoints.Last());

            var position = BezierCurves.CubicCurve(inWorldPosition.ControlPoints.GetBezierValues(), 0.5f);
            var direction = inWorldPosition.ControlPoints.Direction();
            firstLevelSystemPoints.Add(new RiverControlPoint { Position = position - direction * SplitGap, Direction = Quaternion.LookRotation(direction) });
            secondLevelSystemPoints.Insert(0, new RiverControlPoint { Position = position + direction * SplitGap, Direction = Quaternion.LookRotation(direction) });

            levelSystem.ControlPoints = firstLevelSystemPoints;
            levelSystem.UpdateMesh();

            var secondLevelSystem = CreateLevelSystem(levelSystem.transform.position, levelSystem.GetComponent<Renderer>().sharedMaterial);
            secondLevelSystem.ControlPoints = secondLevelSystemPoints;
            secondLevelSystem.UpdateMesh();
        }

        private List<RiverControlPoint> GetControlPointsBetween(LevelSystem levelSystem, RiverControlPoint start, RiverControlPoint end) {
            var result = new List<RiverControlPoint>();

            var currentIndex = levelSystem.ControlPoints.IndexOf(start);

            while (levelSystem.ControlPoints[currentIndex] != end && currentIndex < levelSystem.ControlPoints.Count) {
                result.Add(levelSystem.ControlPoints[currentIndex]);
                currentIndex++;
            }

            result.Add(levelSystem.ControlPoints[currentIndex]);

            return result;
        }

        private void HandleEditAdd(LevelSystem levelSystem, InWorldPosition inWorldPosition) {
            if (inWorldPosition.ControlPoints != null) {
                return;
            }

            Undo.RecordObject(levelSystem, "Added additional control point");
            levelSystem.AddControlPoint(inWorldPosition.Position);
        }

        private void ShowControlPoint(LevelSystem.RiverControlPoint controlPoint, LevelSystem river) {
            var position = river.transform.position + controlPoint.Position;

            var adjustedPosition = river.transform.position + (river.transform.rotation * controlPoint.Position);
            Debug.DrawLine(river.transform.position, adjustedPosition, Color.red);
            Debug.DrawLine(river.transform.position, position, Color.green);

            var originalPosition = adjustedPosition - (river.transform.rotation * controlPoint.Position);
            Debug.DrawLine(adjustedPosition, originalPosition, Color.cyan);

            using (var scope = new EditorGUI.ChangeCheckScope()) {

                controlPoint.Position = Handles.PositionHandle(position, controlPoint.Direction) - river.transform.position;
                controlPoint.Position.y = 0;
                controlPoint.Direction = Handles.Disc(controlPoint.Direction, position, Vector3.up, 3, false, 0);

                if (scope.changed) {
                    Undo.RecordObject(river, "Edited River control point");
                    river.UpdateMesh();
                    EditorUtility.SetDirty(river);
                }
            }
        }

        private InWorldPosition GetInWorldPoint(Vector2 position, LevelSystem river) {
            if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(position), out RaycastHit raycastHit, float.MaxValue, int.MaxValue, QueryTriggerInteraction.Ignore)) {
                if (raycastHit.collider.gameObject == river.gameObject) {
                    return new InWorldPosition {
                        Position = raycastHit.point,
                        IsInWorld = true,
                        IsInSystem = true,
                        ControlPoints = GetSelectedControlPoint(raycastHit, river)
                    };
                } else {
                    return new InWorldPosition {
                        Position = raycastHit.point,
                        IsInWorld = true,
                        IsInSystem = false
                    };
                }
            } else {
                return new InWorldPosition { Position = Vector3.zero, IsInWorld = false };
            }
        }

        private ControlPointPair GetSelectedControlPoint(RaycastHit raycastHit, LevelSystem levelSystem) {
            var triangleIndex = raycastHit.triangleIndex;
            if (levelSystem.SplineCapMode == LevelSystem.SplineCapModeType.Open) {
                triangleIndex -= 4;
            }

            var segmentIndex = triangleIndex / 6;
            var placedSegmentIndex = Mathf.Clamp((segmentIndex + 1) / (levelSystem.SmoothingLevel + 1), 0, int.MaxValue);
            var nextSegmentIndex = Mathf.Clamp(segmentIndex / (levelSystem.SmoothingLevel + 1), 0, int.MaxValue);

            if (nextSegmentIndex == placedSegmentIndex) {
                nextSegmentIndex = (int)Mathf.Repeat(placedSegmentIndex + 1, levelSystem.ControlPoints.Count);
            }

            if(placedSegmentIndex < 0 || placedSegmentIndex >= levelSystem.ControlPoints.Count || nextSegmentIndex < 0 || nextSegmentIndex >= levelSystem.ControlPoints.Count) {
                return null;
            }

            return new ControlPointPair(levelSystem.ControlPoints[placedSegmentIndex], levelSystem.ControlPoints[nextSegmentIndex]);
        }
    }
}
