using MageQuest;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CollisionBear.OpenLevelDraft
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class LevelSystem : MonoBehaviour
    {
        // Each point in the spline system
        [System.Serializable]
        public class RiverControlPoint
        {
            public Vector3 Position;
            public float Width = 1;
            public float Height = 2f;
            public Quaternion Direction = Quaternion.identity;
        }

        public class ControlPointPair
        {
            public RiverControlPoint First;
            public RiverControlPoint Second;

            public ControlPointPair(RiverControlPoint first, RiverControlPoint second)
            {
                First = first;
                Second = second;
            }
        }

        // Class to hold data while dynamically generate the mesh
        public class MeshData
        {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<Vector2> Uvs = new List<Vector2>();
            public List<int> Triangles = new List<int>();

            public int CurrentIndex = 0;
            public float CurrentLeftUvOffset = 0;
            public float CurrentRightUvOffset = 0;
            public float CurrentUvOffset = 0;
        }

        public Material Material;
        public float UvScale = 0.5f;
        public int SmoothingLevel = 4;          // Additional segments inserted between the placed control points. Increased value will cause the river bend smoother.
        public bool IsEditable = false;         // If true, the edit path button is pressed down in the editor and control points are movable/addable/removable

        [HideInInspector]
        public List<RiverControlPoint> ControlPoints;

        public void Reset()
        {
            ControlPoints = new List<RiverControlPoint> {
                new RiverControlPoint{ Position = new Vector3(0f, 0f, 0f), Direction = Quaternion.LookRotation(Vector3.right) }
            };
        }

        public void AddControlPoint(Vector3 position)
        {
            var lastControlPoint = ControlPoints.Last();
            var directionOffset = position - (transform.position + lastControlPoint.Position);
            directionOffset.y = 0;
            var direction = Quaternion.LookRotation(directionOffset);

            var targetPosition = position - transform.position;
            targetPosition.y = lastControlPoint.Position.y;

            // If first, line them up straight
            if (ControlPoints.Count == 1) {
                ControlPoints[0].Direction = direction;
            } else if (ControlPoints.Count > 1) {
                var previous = ControlPoints.Last();
                previous.Direction = Quaternion.Slerp(ControlPoints[ControlPoints.Count -2].Direction, direction, 0.5f);
            }

            var controlPoint = new RiverControlPoint { Position = targetPosition, Direction = direction, Width = lastControlPoint.Width };
            ControlPoints.Add(controlPoint);
            UpdateRiverMesh();
        }

        public void InsertControlPoint(ControlPointPair controlPoints, Vector3 position)
        {

            var targetPosition = position - transform.position;

            var direction = Quaternion.Slerp(controlPoints.First.Direction, controlPoints.Second.Direction, 0.5f);
            var width = Mathf.Lerp(controlPoints.First.Width, controlPoints.Second.Width, 0.5f);

            var controlPoint = new RiverControlPoint { Position = targetPosition, Direction = direction, Width = width };

            var insertIndex = Mathf.Max(ControlPoints.IndexOf(controlPoints.First), ControlPoints.IndexOf(controlPoints.Second));
            ControlPoints.Insert(insertIndex, controlPoint);
            UpdateRiverMesh();
        }

        public void RemoveControlPoint(RiverControlPoint controlPoint)
        {
            ControlPoints.Remove(controlPoint);
            UpdateRiverMesh();
        }

        public void UpdateRiverMesh()
        {
            var meshFilter = GetComponent<MeshFilter>();
            var meshRender = GetComponent<MeshRenderer>();
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
            meshRender.material = Material;
            meshCollider.sharedMesh = null;         // If this step is not taken, Unity might not update the mesh for the collider so it behaves as it had none

            if (mesh.vertexCount > 0) {
                meshCollider.sharedMesh = mesh;
            }
        }

        private MeshData GenerateMeshData(List<RiverControlPoint> controlPoints)
        {
            var result = new MeshData();

            CreateStartCap(controlPoints[0], result);
            CreateEndCap(controlPoints.Last(), result);

            AddControlPointToMesh(controlPoints[0], null, result);
            for (int i = 1; i < controlPoints.Count; i++) {
                AddControlPointToMesh(controlPoints[i], controlPoints[i - 1], result);
            }

            return result;
        }

        private float GetUvOffset(RiverControlPoint controlPoint, RiverControlPoint lastControlPoint)
        {
            if (lastControlPoint == null) {
                return 0;
            }

            return (lastControlPoint.Position - controlPoint.Position).magnitude;
        }

        private void CreateStartCap(RiverControlPoint controlPoint, MeshData meshData)
        {
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);
            meshData.Normals.Add(controlPoint.Direction * Vector3.back);

            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 0), controlPoint.Width));

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

        private void CreateEndCap(RiverControlPoint controlPoint, MeshData meshData)
        {
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);
            meshData.Normals.Add(Vector3.back);

            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position,controlPoint.Direction, new Vector3(1, 0), controlPoint.Width));

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

        private void AddControlPointToMesh(RiverControlPoint controlPoint, RiverControlPoint lastControlPoint, MeshData meshData)
        {
            var uvOffset = GetUvOffset(controlPoint, lastControlPoint);
            meshData.CurrentUvOffset += uvOffset;
            meshData.CurrentLeftUvOffset += uvOffset;
            meshData.CurrentRightUvOffset += uvOffset;

            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 0), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(-1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 2), controlPoint.Width));
            meshData.Vertices.Add(GetVectorPosition(controlPoint.Position, controlPoint.Direction, new Vector3(1, 0), controlPoint.Width));

            // All river segments points up regardless of actual orientation
            meshData.Normals.Add(controlPoint.Direction * Vector3.left);
            meshData.Normals.Add(controlPoint.Direction * Vector3.left);
            meshData.Normals.Add(controlPoint.Direction * Vector3.up);
            meshData.Normals.Add(controlPoint.Direction * Vector3.up);
            meshData.Normals.Add(controlPoint.Direction * Vector3.right);
            meshData.Normals.Add(controlPoint.Direction * Vector3.right);

            // Make the X axis a continuous point along the edge and the y axis continuous along the middle. Makes it looks smoother
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, controlPoint.Width * 2) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentRightUvOffset, 0) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, controlPoint.Width * 2) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentRightUvOffset, 0) * UvScale);
            meshData.Uvs.Add(new Vector2(meshData.CurrentLeftUvOffset, controlPoint.Width * 2) * UvScale);
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

        private List<RiverControlPoint> GenerateRiverControlPoints(List<RiverControlPoint> controlPoints, int steps)
        {
            var result = new List<RiverControlPoint> {
                controlPoints[0]
            };

            if (controlPoints.Count < 2) {
                return result;
            }

            foreach (var pair in GetControlPointPairs(controlPoints)) {

                var pairHalfDistance = (pair.Second.Position - pair.First.Position).magnitude / 4;
                var pairStepDistance = 1f / (steps + 1);

                var firstPoint = pair.First.Position;
                var lastPoint = pair.Second.Position;
                var extraPosition01 = pair.First.Position + pair.First.Direction * Vector3.forward * pairHalfDistance;
                var extraPosition02 = pair.Second.Position + pair.Second.Direction * Vector3.back * pairHalfDistance;

                for (int i = 0; i < steps; i++) {
                    var distanceFactor = (i + 1) * pairStepDistance;
                    var position = BezierCurves.CubicCurve(firstPoint, extraPosition01, extraPosition02, lastPoint, distanceFactor);
                    var tangent = BezierCurves.CubicCurveDerivative(firstPoint, extraPosition01, extraPosition02, lastPoint, distanceFactor).normalized;
                    var width = Mathf.Lerp(pair.First.Width, pair.Second.Width, distanceFactor);

                    result.Add(new RiverControlPoint { Position = position, Direction = Quaternion.LookRotation(tangent), Width = width });
                }

                result.Add(pair.Second);
            }

            return result;
        }

        private Vector3 GetVectorPosition(Vector3 position, Quaternion rotation, Vector3 direction, float width)
        {
            return position + rotation * direction * width;
        }

        public List<ControlPointPair> GetControlPointPairs(List<LevelSystem.RiverControlPoint> controlPoints)
        {
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