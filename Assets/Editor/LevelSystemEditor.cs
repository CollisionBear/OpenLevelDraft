using System.Linq;
using UnityEditor;
using UnityEngine;

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
            levelSystem.IsEditable = true;
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
            public bool IsInRiver;
            public LevelSystem.ControlPointPair ControlPoints;
        }

        private readonly RaycastHit[] RaycastHits = new RaycastHit[128];
        private GUIStyle EditorTextStyle;

        public override void OnInspectorGUI()
        {
            var river = target as LevelSystem;
            EditorGUI.BeginChangeCheck();
            river.Material = EditorGUILayout.ObjectField("Material", river.Material, typeof(Material), false) as Material;
            river.UvScale = EditorGUILayout.FloatField("UV Scale", river.UvScale);
            river.SmoothingLevel = Mathf.Clamp(EditorGUILayout.IntField("Smoothing Level", river.SmoothingLevel), 0, 10);
            EditorGUILayout.Space();

            river.IsEditable = GUILayout.Toggle(river.IsEditable, "Edit path\t(E)", "Button", GUILayout.Height(24));

            var currentEvent = Event.current;
            if(currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Space) {
                river.IsEditable = true;
            }

            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(river, "Edited River Material");
                river.UpdateRiverMesh();
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

            river.IsEditable = Selection.activeGameObject == river.gameObject;
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

            var river = target as LevelSystem;
            if(river == null) {
                return;
            }

            var currentEvent = Event.current;

            if (!river.IsEditable) {
                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.E) {
                    river.IsEditable = true;
                    currentEvent.Use();
                }

                return;
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            foreach (var point in river.ControlPoints) {
                ShowControlPoint(point, river);
            }

            DrawCurvedLine(river);

            var inWorldPosition = GetInWorldPoint(currentEvent.mousePosition, river);

            if (currentEvent.control) {
                var lastPoint = river.ControlPoints.Last();
                var lastPointPosition = river.transform.position + lastPoint.Position;

                if (inWorldPosition.IsInRiver) {
                    if (currentEvent.type == EventType.MouseDown) {
                        Undo.RecordObject(river, "Inserted control point");
                        river.InsertControlPoint(inWorldPosition.ControlPoints, inWorldPosition.Position);
                        currentEvent.Use();
                    }
                } else {
                    Handles.DrawLine(lastPointPosition, inWorldPosition.Position);

                    if (currentEvent.type == EventType.MouseDown) {
                        Undo.RecordObject(river, "Added additional control point");
                        river.AddControlPoint(inWorldPosition.Position);
                        currentEvent.Use();
                    }
                }
            } else if (currentEvent.shift) {
                if (currentEvent.type == EventType.MouseDown) {
                    Undo.RecordObject(river, "Removed control point");
                    river.RemoveControlPoint(inWorldPosition.ControlPoints.First);
                    currentEvent.Use();
                }

            } else {
                Handles.Label(inWorldPosition.Position + Vector3.down * 2, "Hold control to place point\nHold shift to remove a point\nPress space to release", EditorTextStyle);
            }

            if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.Space || currentEvent.keyCode == KeyCode.Escape)) {
                river.IsEditable = false;
                currentEvent.Use();
            }

            sceneView.Repaint();
        }

        private void DrawStraightLine(LevelSystem river)
        {
            foreach (var pair in river.GetControlPointPairs(river.ControlPoints)) {
                Handles.DrawLine(river.transform.position + pair.First.Position, river.transform.position + pair.Second.Position);
            }
        }

        private void DrawCurvedLine(LevelSystem river)
        {
            var leftRotation = Quaternion.LookRotation(Vector3.left);
            var rightotation = Quaternion.LookRotation(Vector3.right);

            foreach (var pair in river.GetControlPointPairs(river.ControlPoints)) {
                var distance = (pair.Second.Position - pair.First.Position).magnitude / 3;
                var firstPoint = pair.First.Position + river.transform.position;
                var lastPoint = pair.Second.Position + river.transform.position;
                var extraPosition01 = pair.First.Position + pair.First.Direction * Vector3.forward * distance + river.transform.position;
                var extraPosition02 = pair.Second.Position + pair.Second.Direction * Vector3.back * distance + river.transform.position;

                Handles.DrawBezier(firstPoint, lastPoint, extraPosition01, extraPosition02, Color.green, null, 2);
            }
        }

        private float GetVelocity(float value)
        {
            return 0;
        }

        private void ShowControlPoint(LevelSystem.RiverControlPoint controlPoint, LevelSystem river)
        {
            var position = river.transform.position + controlPoint.Position;
            float size = HandleUtility.GetHandleSize(position) * 1f;

            EditorGUI.BeginChangeCheck();
            controlPoint.Position = Handles.DoPositionHandle(position, controlPoint.Direction) - river.transform.position;
            controlPoint.Position.y = 0;
            controlPoint.Direction = Handles.Disc(controlPoint.Direction, position, Vector3.up, 2, false, 0);
            //controlPoint.Width = Mathf.Clamp(Handles.ScaleSlider(controlPoint.Width, position, controlPoint.Direction * Vector3.left, controlPoint.Direction, controlPoint.Width, 0), 0.2f, float.MaxValue);
            Handles.DrawLine(position + controlPoint.Direction * Vector3.left * controlPoint.Width, position);
            Handles.DrawLine(position + controlPoint.Direction * Vector3.right * controlPoint.Width, position);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(river, "Edited River control point");
                river.UpdateRiverMesh();
                EditorUtility.SetDirty(river);
            }
        }

        private InWorldPosition GetInWorldPoint(Vector2 position, LevelSystem river)
        {
            if (Physics.Raycast(HandleUtility.GUIPointToWorldRay(position), out RaycastHit raycastHit)) {
                if (raycastHit.collider.gameObject == river.gameObject) {
                    return new InWorldPosition { Position = raycastHit.point, IsInWorld = true, IsInRiver = true, ControlPoints = GetSelectedControlPoint(raycastHit, river) };
                } else {
                    return new InWorldPosition { Position = raycastHit.point, IsInWorld = true, IsInRiver = false };
                }
            } else {
                return new InWorldPosition { Position = Vector3.zero, IsInWorld = false };
            }
        }

        private LevelSystem.ControlPointPair GetSelectedControlPoint(RaycastHit raycastHit, LevelSystem river)
        {
            var segmentIndex = (raycastHit.triangleIndex - 4) / 6;
            var placedSegmentIndex = Mathf.Clamp((segmentIndex + 1) / (river.SmoothingLevel + 1), 0, int.MaxValue);
            var previousSegmentIndex = Mathf.Clamp(segmentIndex / (river.SmoothingLevel + 1), 0, int.MaxValue);

            if (previousSegmentIndex == placedSegmentIndex) {
                previousSegmentIndex = placedSegmentIndex + 1;
            }

            return new LevelSystem.ControlPointPair(river.ControlPoints[placedSegmentIndex], river.ControlPoints[previousSegmentIndex]);
        }
    }
}
