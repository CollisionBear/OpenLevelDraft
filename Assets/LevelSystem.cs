using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CollisionBear.OpenLevelDraft {
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class LevelSystem : MonoBehaviour {
        // Each point in the spline system
        [Serializable]
        public class RiverControlPoint {
            public int Index;
            public Vector3 Position;
            public Quaternion Direction = Quaternion.identity;

            public override string ToString() => Position.ToString();
        }

        public class BezierPosition {
            public Vector3 Start;
            public Vector3 StartTangent;
            public Vector3 EndTangent;
            public Vector3 End;
        }

        public class ControlPointPair {
            public RiverControlPoint First;
            public RiverControlPoint Second;

            public ControlPointPair(RiverControlPoint first, RiverControlPoint second) {
                First = first;
                Second = second;
            }

            public Vector3 Middle() => Vector3.Lerp(Second.Position, First.Position, 0.5f);
            public Vector3 Direction() => (Second.Position - First.Position).normalized;

            public BezierPosition GetBezierValues() {
                var result = new BezierPosition();

                var pairHalfDistance = (Second.Position - First.Position).magnitude / 4;

                result.Start = First.Position;
                result.StartTangent = First.Position + First.Direction * Vector3.forward * pairHalfDistance;
                result.EndTangent = Second.Position + Second.Direction * Vector3.back * pairHalfDistance;
                result.End = Second.Position;

                return result;
            }
        }

        // Class to hold data while dynamically generate the mesh
        public class MeshData {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<Vector2> Uvs = new List<Vector2>();
            public List<int> Triangles = new List<int>();

            public int CurrentIndex = 0;
            public float CurrentLeftUvOffset = 0;
            public float CurrentRightUvOffset = 0;
            public float CurrentUvOffset = 0;
        }

        public enum SplineCapModeType : byte {
            Open = 0,
            Closed = 1
        }

        public enum SplineToolType : byte {
            None = 0,
            Edit = 1,
            Split = 2
        }

        [Flags]
        public enum ControlPointOptions : byte {
            LockHeight = 1
        }

        public float Width = 1f;
        public float Height = 1f;
        public SplineCapModeType SplineCapMode = SplineCapModeType.Open;

        public ControlPointOptions Options = 0;

        public float UvScale = 0.5f;
        public int SmoothingLevel = 10;          // Additional segments inserted between the placed control points. Increased value will cause the river bend smoother.
        public SplineToolType Tool = SplineToolType.None;

        [HideInInspector]
        public List<RiverControlPoint> ControlPoints;

        public void Reset() {
            ControlPoints = new List<RiverControlPoint> {
                new RiverControlPoint{ Position = new Vector3(0f, 0f, 0f), Direction = Quaternion.LookRotation(Vector3.right) }
            };
        }

        public void AddControlPoint(Vector3 position) {
            var lastControlPoint = ControlPoints.Last();
            var directionOffset = position - (transform.position + lastControlPoint.Position);
            directionOffset.y = 0;
            var direction = Quaternion.LookRotation(directionOffset);

            var targetPosition = position - transform.position;

            if (Options.HasFlag(ControlPointOptions.LockHeight)) {
                targetPosition.y = lastControlPoint.Position.y;
            }

            // If first, line them up straight
            if (ControlPoints.Count == 1) {
                ControlPoints[0].Direction = direction;
            } else if (ControlPoints.Count > 1) {
                var previous = ControlPoints.Last();
                previous.Direction = Quaternion.Slerp(ControlPoints[ControlPoints.Count - 2].Direction, direction, 0.5f);
            }

            var controlPoint = new RiverControlPoint { Position = targetPosition, Direction = direction };
            ControlPoints.Add(controlPoint);
            UpdateMesh();
        }

        public void InsertControlPoint(ControlPointPair controlPoints, Vector3 position) {
            var targetPosition = position - transform.position;

            if (Options.HasFlag(ControlPointOptions.LockHeight)) {
                targetPosition.y = controlPoints.First.Position.y;
            }

            var direction = Quaternion.Slerp(controlPoints.First.Direction, controlPoints.Second.Direction, 0.5f);

            var controlPoint = new RiverControlPoint { Position = targetPosition, Direction = direction };

            var firstIndex = ControlPoints.IndexOf(controlPoints.First);
            var secondIndex = ControlPoints.IndexOf(controlPoints.Second);

            var insertIndex = Mathf.Max(firstIndex, secondIndex);

            if (controlPoints.Second == ControlPoints.First()) {
                // Closed loop, in between last and first control point
                ControlPoints.Add(controlPoint);
            } else {
                ControlPoints.Insert(insertIndex, controlPoint);
            }
            UpdateMesh();
        }

        public void RemoveControlPoint(RiverControlPoint controlPoint) {
            ControlPoints.Remove(controlPoint);
            UpdateMesh();
        }

        public void UpdateMesh() {
            var meshFilter = GetComponent<MeshFilter>();

            var meshCollider = GetComponent<MeshCollider>();

            var roundedControlPoints = GenerateRiverControlPoints(ControlPoints, SmoothingLevel);

            var meshData = GenerateMeshData(roundedControlPoints);
            var mesh = new Mesh {
                vertices = meshData.Vertices.ToArray(),
                normals = meshData.Normals.ToArray(),
                uv = meshData.Uvs.ToArray(),
                triangles = meshData.Triangles.ToArray()
            };

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = null;         // If this step is not taken, Unity might not update the mesh for the collider so it behaves as it had none

            if (mesh.vertexCount > 0) {
                meshCollider.sharedMesh = mesh;
            }
        }

        private MeshData GenerateMeshData(List<RiverControlPoint> controlPoints) {
            var result = new MeshData();

            if (SplineCapMode == SplineCapModeType.Open) {
                CreateStartCap(controlPoints[0], result);
                CreateEndCap(controlPoints.Last(), result);
            }

            AddControlPointToMesh(controlPoints[0], null, result);
            for (int i = 1; i < controlPoints.Count; i++) {
                AddControlPointToMesh(controlPoints[i], controlPoints[i - 1], result);
            }

            return result;
        }

        private float GetUvOffset(RiverControlPoint controlPoint, RiverControlPoint lastControlPoint) {
            if (lastControlPoint == null) {
                return 0;
            }

            return (lastControlPoint.Position - controlPoint.Position).magnitude;
        }

        private void CreateStartCap(RiverControlPoint controlPoint, MeshData meshData) {
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);

            var scale = new Vector2(Width, Height);
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 0), scale));

            meshData.Uvs.Add(new Vector2(0, 0) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(0, 1) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(1, 1) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(1, 0) * 2f * UvScale);

            meshData.Triangles.AddRange(new List<int> {
                0, 1, 3,
                1, 2, 3
            }.Select(v => meshData.CurrentIndex + v));

            meshData.CurrentIndex += 4;
        }

        private void CreateEndCap(RiverControlPoint controlPoint, MeshData meshData) {
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);

            var scale = new Vector2(Width, Height);
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 0), scale));

            meshData.Uvs.Add(new Vector2(1, 0) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(1, 1) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(0, 1) * 2f * UvScale);
            meshData.Uvs.Add(new Vector2(0, 0) * 2f * UvScale);

            meshData.Triangles.AddRange(new List<int> {
                0, 3, 1,
                1, 3, 2
            }.Select(v => meshData.CurrentIndex + v));

            meshData.CurrentIndex += 4;
        }

        private void AddControlPointToMesh(RiverControlPoint controlPoint, RiverControlPoint lastControlPoint, MeshData meshData) {
            var uvOffset = GetUvOffset(controlPoint, lastControlPoint);
            meshData.CurrentUvOffset += uvOffset;
            meshData.CurrentLeftUvOffset += uvOffset;
            meshData.CurrentRightUvOffset += uvOffset;

            var scale = new Vector2(Width, Height);
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), scale));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 0), scale));

            // All river segments points up regardless of actual orientation
            meshData.Normals.Add(controlPoint.Direction * Vector3.left);
            meshData.Normals.Add(controlPoint.Direction * Vector3.left);
            meshData.Normals.Add(controlPoint.Direction * Vector3.up);
            meshData.Normals.Add(controlPoint.Direction * Vector3.up);
            meshData.Normals.Add(controlPoint.Direction * Vector3.right);
            meshData.Normals.Add(controlPoint.Direction * Vector3.right);

            // Make the X axis a continuous point along the edge and the y axis continuous along the middle. Makes it looks smoother
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, Width * 2) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentRightUvOffset, 0) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, Width * 2) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentRightUvOffset, 0) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, Width * 2) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentRightUvOffset, 0) * UvScale);

            var indicesPerSegment = 6;
            var indexOffset = new List<int> {
                0, 6, 1,
                6, 7, 1,
                2, 8, 3,
                8, 9, 3,
                4, 10, 5,
                10, 11, 5
            };

            if (lastControlPoint != null) {
                meshData.Triangles.AddRange(indexOffset.Select(v => meshData.CurrentIndex + (v - indicesPerSegment)));
            }

            meshData.CurrentIndex += indicesPerSegment;
        }

        private List<RiverControlPoint> GenerateRiverControlPoints(List<RiverControlPoint> controlPoints, int steps) {
            var result = new List<RiverControlPoint> {
                controlPoints[0]
            };

            if (controlPoints.Count < 2) {
                return result;
            }

            foreach (var pair in GetControlPointPairs(controlPoints)) {
                result.AddRange(GenereateStepPoints(pair, steps));
            }

            if (SplineCapMode == SplineCapModeType.Closed) {
                result.AddRange(GenereateStepPoints(new ControlPointPair(controlPoints.Last(), controlPoints.First()), steps));
            }

            return result;
        }

        private List<RiverControlPoint> GenereateStepPoints(ControlPointPair pair, int steps) {
            var result = new List<RiverControlPoint>();

            var pairStepDistance = 1f / (steps + 1);
            var bezierPosition = pair.GetBezierValues();

            for (int i = 0; i < steps; i++) {
                var distanceFactor = (i + 1) * pairStepDistance;
                var position = BezierCurves.CubicCurve(bezierPosition, distanceFactor);
                var tangent = BezierCurves.CubicCurveDerivative(bezierPosition, distanceFactor).normalized;

                result.Add(new RiverControlPoint { Position = position, Direction = Quaternion.LookRotation(tangent) });
            }

            result.Add(pair.Second);

            return result;
        }

        private Vector3 GetVectorPosition(Vector3 position, Quaternion rotation, Vector3 direction, Vector2 scale) {
            var offset = direction;
            offset.x *= scale.x;
            offset.y *= scale.y;
            return position + rotation * offset;
        }

        public List<ControlPointPair> GetControlPointPairs(List<LevelSystem.RiverControlPoint> controlPoints) {
            var result = new List<ControlPointPair>();
            if (controlPoints.Count < 2) {
                return result;
            }

            for (int i = 0; i < controlPoints.Count - 1; i++) {
                result.Add(new ControlPointPair(controlPoints[i], controlPoints[i + 1]));
            }

            return result;
        }
    }
}