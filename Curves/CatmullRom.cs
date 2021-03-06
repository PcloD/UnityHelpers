﻿/* 
 * Curve tool for Unity to easily visualize smooth curves
 * and make actors follow them. Large parts of this script (specifically Evaluate functions) were inspired by
 * Nick Hall's work on splines that can be found at https://github.com/nickhall/Unity-Procedural.
 * 
 * If you want to be able to see the curves in the Unity Scene, call Draw()
 * from a MonoBehaviour class's OnDrawGizmos(). 
 * 
 * https://github.com/tombbonin
 */

using UnityEngine;

namespace Curves
{
    public class CatmullRom : Curve
    {
        public bool CloseLoop;
        private float[] CPDists;
        private CurvePoint[] _curvePoints;

        public CatmullRom(int resolution, Vector3[] controlPoints, bool closeLoop = false) : base(resolution, controlPoints)
        {
            CloseLoop = closeLoop;
            MeasureCurve();
        }

        public override void MeasureCurve()
        {
            var pointsOnCurve = GetCurvePoints();
            var nbPoints = CloseLoop ? ControlPoints.Length : ControlPoints.Length - 1;
            CPDists = new float[nbPoints + 1];
            var idx = 0;
            for (var i = 0; i < pointsOnCurve.Length; i += Resolution)
            {
                CPDists[idx] = pointsOnCurve[i].DistanceOnCurve;
                idx++;
            }
            Length = pointsOnCurve[pointsOnCurve.Length - 1].DistanceOnCurve;
        }

        private static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 tanP0, Vector3 tanP2, float t)
        {
            // Catmull-Rom splines are Hermite curves with special tangent values.
            // Hermite curve formula:
            // (2t^3 - 3t^2 + 1) * p0 + (t^3 - 2t^2 + t) * m0 + (-2t^3 + 3t^2) * p1 + (t^3 - t^2) * m1
            // For points p0 and p1 passing through points m0 and m1 interpolated over t = [0, 1]
            // Tangent M[k] = (P[k+1] - P[k-1]) / 2
            // With [] indicating subscript
            Vector3 position = (2.0f * t * t * t - 3.0f * t * t + 1.0f) * p0
                             + (t * t * t - 2.0f * t * t + t) * tanP0
                             + (-2.0f * t * t * t + 3.0f * t * t) * p1
                             + (t * t * t - t * t) * tanP2;
            return position;
        }

        private static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 tanP0, Vector3 tanP1, float t, out Vector3 tangent)
        {
            // Calculate tangents
            // p'(t) = (6t² - 6t)p0 + (3t² - 4t + 1)m0 + (-6t² + 6t)p1 + (3t² - 2t)m1
            tangent = (6 * t * t - 6 * t) * p0
                   + (3 * t * t - 4 * t + 1) * tanP0
                   + (-6 * t * t + 6 * t) * p1
                   + (3 * t * t - 2 * t) * tanP1;
            return Evaluate(p0, p1, tanP0, tanP1, t);
        }

        private static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 tanP0, Vector3 tanP1, float t, out Vector3 tangent, out Vector3 curvature)
        {
            // Calculate second derivative (curvature)
            // p''(t) = (12t - 6)p0 + (6t - 4)m0 + (-12t + 6)p1 + (6t - 2)m1
            curvature = (12 * t - 6) * p0
                     + (6 * t - 4) * tanP0
                     + (-12 * t + 6) * p1
                     + (6 * t - 2) * tanP1;
            return Evaluate(p0, p1, tanP0, tanP1, t, out tangent);
        }

        public override Vector3 Evaluate(float t, out Vector3 tangent, out Vector3 curvature)
        {
            var posOnCurve = t * Length;
            var i = 0;
            while (i < CPDists.Length - 1 && posOnCurve >= CPDists[i])
                i++;

            var p0Dist = CPDists[i - 1];
            var p1Dist = CPDists[i];
            var localT = (posOnCurve - p0Dist) / (p1Dist - p0Dist); // same as inverseLerp

            var p0 = ControlPoints[i - 1];
            var p1 = ControlPoints[GetClampedPointIdx(i)];
            var m0 = 0.5f * (p1 - ControlPoints[GetClampedPointIdx(i - 2)]);
            var m1 = 0.5f * (ControlPoints[GetClampedPointIdx(i + 1)] - p0);

            return Evaluate(p0, p1, m0, m1, localT, out tangent, out curvature);
        }

        private int GetClampedPointIdx(int pointIdx)
        {
            //Clamp the list positions to allow looping
            //start over again when reaching the end or beginning
            if (pointIdx < 0)
                return CloseLoop ? ControlPoints.Length + pointIdx : 0;
            if (pointIdx >= ControlPoints.Length)
                return CloseLoop ? pointIdx % ControlPoints.Length : ControlPoints.Length - 1;
            return pointIdx;
        }

        public static Vector3[] GetCurvePositions(int resolution, Vector3[] controlPoints, bool closeLoop = false)
        {
            var catmullRom = new CatmullRom(resolution, controlPoints, closeLoop);
            return GetCurvePositions(catmullRom, resolution, controlPoints);
        }

        public override CurvePoint[] GetCurvePoints()
        {
            // avoids rebuilding entire curve on every call, should be cleared if we wanted to rebuild the curve (if control points move for example)
            if (_curvePoints != null)
                return _curvePoints;

            // First for loop goes through each control point, the second subdivides the path between CPs based on resolution
            var distanceOnCurve = 0f;
            var prevPoint = new CurvePoint { Position = ControlPoints[0] };
            // If we are looping, we are adding an extra segment, so we need an extra point
            var nbPoints = CloseLoop ? ControlPoints.Length : ControlPoints.Length - 1;
            var points = new CurvePoint[nbPoints * Resolution + 1];
            Vector3 p0 = Vector3.zero, p1 = Vector3.zero, m0 = Vector3.zero, m1 = Vector3.zero;
            for (var i = 0; i < nbPoints; i++)
            {
                p0 = ControlPoints[i];
                p1 = ControlPoints[GetClampedPointIdx(i + 1)];
                m0 = 0.5f * (p1 - ControlPoints[GetClampedPointIdx(i - 1)]);
                m1 = 0.5f * (ControlPoints[GetClampedPointIdx(i + 2)] - p0);
                // Second for loop actually creates the spline for this particular segment
                for (var j = 0; j < Resolution; j++)
                    points[i * Resolution + j] = GetPointOnCurve(p0, p1, m0, m1, (float)j / Resolution, ref distanceOnCurve, ref prevPoint);
            }
            // we have to manually add the last point on the spline
            points[nbPoints * Resolution] = GetPointOnCurve(p0, p1, m0, m1, 1f, ref distanceOnCurve, ref prevPoint);
            FixNormals(ref points);
            _curvePoints = points;
            return points;
        }

        private static CurvePoint GetPointOnCurve(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t, ref float distanceOnCurve, ref CurvePoint prevPoint)
        {
            var point = new CurvePoint();
            point.Position = Evaluate(p0, p1, m0, m1, t, out point.Tangent, out point.Curvature);
            point.Bank = GetBankAngle(point.Tangent, point.Curvature, MaxBankAngle);

            // Currently breaks if 3 consecutive points are colinear, to be improved with second pass on curve
            point.Normal = Vector3.Cross(point.Curvature, point.Tangent).normalized;
            if (Vector3.Dot(point.Normal, prevPoint.Normal) < 0)
                point.Normal *= -1;

            distanceOnCurve += Vector3.Distance(point.Position, prevPoint.Position);
            point.DistanceOnCurve = distanceOnCurve;
            prevPoint = point;
            return point;
        }

        public static CurvePoint[] GetCurvePoints(int resolution, Vector3[] controlPoints, bool closeLoop = false)
        {
            var curve = new CatmullRom(resolution, controlPoints, closeLoop);
            return curve.GetCurvePoints();
        }
    }
}