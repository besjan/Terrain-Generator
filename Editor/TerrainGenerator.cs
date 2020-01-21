﻿namespace Cuku
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;
    using System.IO;
    using System;
    using System.Linq;

    public class TerrainGenerator
    {
        #region Properties
        static string DataPath = "Assets/StreamingAssets/Test";

        static Transform Terrain;

        static List<Patch> TilesWithMissingPoints;

        static GameObject TileObjectPrefab;

        static int PatchResolution = 2000; // 2km

        static int TileResolution = 201;
        static int TilesPerPatch = 10;

        static int CentreTileLon = 392000;
        static int CentreTileLat = 5820000;
        #endregion

        static string[] Initialize(string terrainName)
        {
            Terrain = new GameObject(terrainName).transform;

            TilesWithMissingPoints = new List<Patch>();

            TileObjectPrefab = Resources.Load<GameObject>("Tile");

            var dataPaths = Directory.GetFiles(DataPath, "*.txt");

            return dataPaths;
        }

        [MenuItem("Cuku/Generate Terrain From DGM (1m grid)")]
        static void GenerateTerrainFromDGM1()
        {
            var filePaths = Initialize("Terrain DGM (1m grid)");

            for (int filePath = 0; filePath < filePaths.Length; filePath++)
            {
                CreateTiles(filePaths[filePath]);
            }
        }

        static void CreateTiles(string filePath)
        {
            // Get patch coordinates
            var coordinates = Path.GetFileNameWithoutExtension(filePath).Split(new char[] { '_' });
            var patchLon = Convert.ToInt32(coordinates[0]) * 1000;
            var patchLat = Convert.ToInt32(coordinates[1]) * 1000;

            // Create patch game object
            var patch = new GameObject(patchLon + "_" + patchLat).transform;
            patch.SetParent(Terrain);

            Patch tilesWithMissingPoints = new Patch()
            {
                Lon = patchLon,
                Lat = patchLat,
                Tiles = new Tile[2 * TilesPerPatch - 1]
            };
            int tilesWithMissingPointsId = 0;

            // Create tile game objects
            var tiles = new Tile[TilesPerPatch * TilesPerPatch];
            var TileCoordinateStep = PatchResolution / TilesPerPatch;
            int tileId = 0;

            // Loop vertical tiles
            for (int vTile = 0; vTile < TilesPerPatch; vTile++)
            {
                var tileLat = patchLat + vTile * TileCoordinateStep;

                // Loop horizontal tiles
                for (int hTile = 0; hTile < TilesPerPatch; hTile++)
                {
                    var tileLon = patchLon + hTile * TileCoordinateStep;

                    var tileMesh = CreateTile(patch, tileLon, tileLat);
                    var tile = new Tile()
                    {
                        Lon = tileLon,
                        Lat = tileLat,
                        MeshFilter = tileMesh
                    };

                    tiles[tileId] = tile;
                    tileId++;

                    // Add top row tiles and right column tiles to tiles with missing points list
                    if (vTile == TilesPerPatch - 1 || hTile == TilesPerPatch - 1)
                    {
                        tilesWithMissingPoints.Tiles[tilesWithMissingPointsId] = tile;
                        tilesWithMissingPointsId++;
                    }
                }
            }

            TilesWithMissingPoints.Add(tilesWithMissingPoints);

            // Move tile points
            using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var pointString = line.Split(new char[] { ' ' });
                    var lon = Convert.ToInt32(pointString[0]);
                    var lat = Convert.ToInt32(pointString[1]);
                    var height = float.Parse(pointString[2]);

                    //var relatedTiles = GetPointTileMeshes(lon, lat, new Tile[] { tiles[0] });
                    var relatedTiles = GetPointTileMeshes(lon, lat, tiles);
                    if (relatedTiles == null) continue;
                    for (int i = 0; i < relatedTiles.Length; i++)
                    {
                        MoveTilePoint(lon, lat, height, relatedTiles[i]);
                    }                
                }
            }
        }

        static MeshFilter CreateTile(Transform patch, int longitude, int latitude)
        {
            // Create mesh
            var mesh = CreateTileMesh(longitude + "_" + latitude);

            // Create object
            var tileObject = GameObject.Instantiate<GameObject>(TileObjectPrefab, patch);
            tileObject.name = longitude + "_" + latitude;
            tileObject.GetComponent<MeshFilter>().sharedMesh = mesh;

            // Position relative to centre tile
            var posX = longitude - CentreTileLon;
            var posZ = latitude - CentreTileLat;
            tileObject.transform.position = new Vector3(Mathf.Round(posX), 0, Mathf.Round(posZ));

            return tileObject.GetComponent<MeshFilter>();
        }

        static Mesh CreateTileMesh(string name)
        {
            var mesh = new Mesh();
            mesh.name = name;

            var hRes = TileResolution;
            var vRes = TileResolution;

            var length = vRes - 1;
            var width = hRes - 1;

            #region Vertices		
            Vector3[] vertices = new Vector3[hRes * vRes];
            // Loop columns
            for (int clm = 0; clm < hRes; clm++)
            {
                float vPos = ((float)clm / (hRes - 1)) * length;
                // Loop rows
                for (int row = 0; row < vRes; row++)
                {
                    float hPos = ((float)row / (vRes - 1)) * width;
                    var id = row + clm * vRes;
                    vertices[id] = new Vector3(hPos, 0, vPos);
                }
            }
            #endregion

            #region Normals
            Vector3[] normals = new Vector3[vertices.Length];
            for (int n = 0; n < normals.Length; n++)
            {
                normals[n] = Vector3.up;
            }
            #endregion

            #region UVs		
            Vector2[] uvs = new Vector2[vertices.Length];
            for (int v = 0; v < hRes; v++)
            {
                for (int u = 0; u < vRes; u++)
                {
                    uvs[u + v * vRes] = new Vector2((float)u / (vRes - 1), (float)v / (hRes - 1));
                }
            }
            #endregion

            #region Triangles
            int nbFaces = (vRes - 1) * (hRes - 1);
            int[] triangles = new int[nbFaces * 6];
            int t = 0;
            for (int face = 0; face < nbFaces; face++)
            {
                // Retrieve lower left corner from face ind
                int i = face % (vRes - 1) + (face / (hRes - 1) * vRes);

                triangles[t++] = i + vRes;
                triangles[t++] = i + 1;
                triangles[t++] = i;

                triangles[t++] = i + vRes;
                triangles[t++] = i + vRes + 1;
                triangles[t++] = i + 1;
            }
            #endregion

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            //mesh.RecalculateBounds();
            //mesh.Optimize();

            AssetDatabase.CreateAsset(mesh, "Assets/Resources/" + name + ".asset");
            AssetDatabase.Refresh();

            return mesh;
        }

        static Tile[] GetPointTileMeshes(int pointLon, int pointLat, Tile[] tiles)
        {
            var relatedTiles = tiles.Where(tile => tile.Lon <= pointLon && pointLon < tile.Lon + TileResolution &&
            tile.Lat <= pointLat && pointLat < tile.Lat + TileResolution).ToArray();

            if (relatedTiles.Length == 0)
            {
                //Debug.LogWarning("No tiles where found for point " + pointLon + "_" + pointLat);
                return null;
            }

            return relatedTiles;
        }

        static void MoveTilePoint(int pointLon, int pointLat, float height, Tile tile)
        {
            var row = pointLat - tile.Lat;
            var clm = pointLon - tile.Lon;

            var pointId = row * TileResolution + clm;

            var vertices = tile.MeshFilter.sharedMesh.vertices;
            vertices[pointId].y = height;
            tile.MeshFilter.sharedMesh.vertices = vertices;
        }

        /*   
           #region Points
           /// <summary>
           /// Missing points for each tile are:
           /// first row of top tile,
           /// first column of right tile,
           /// first point of top right tile.
           /// </summary>
           /// <param name="filePath"></param>
           static void ExtractPatchMissingPoints(string filePath)
           {
               var coordinates = GetCoordinates(filePath);
               var points = GetPatchPoints(filePath);

               rowPatchMissingPoints.Add(coordinates, points.Where(p => p.Y == coordinates.Lat * 1000).ToArray());
               columnPatchMissingPoints.Add(coordinates, points.Where(p => p.X == coordinates.Lon * 1000).ToArray());
               anglePatchMissingPoints.Add(coordinates, points.FirstOrDefault(p => p.X == coordinates.Lon * 1000 && p.Y == coordinates.Lat * 1000));
           }

           static Tuple<bool[], Point[]> GetAllPoints(string filePath, Coordinates coordinates)
           {
               var allPoints = new List<Point>();

               // Get file points
               allPoints.AddRange(GetPatchPoints(filePath));

               // Get missing points from other patches
               var missingPoints = GetMissingPoints(coordinates);
               allPoints.AddRange(missingPoints.Item2);

               return new Tuple<bool[], Point[]>(missingPoints.Item1, allPoints.ToArray());
           }

           static Point[] GetPatchPoints(string filePath)
           {
               List<Point> points = new List<Point>();
               using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
               using (BufferedStream bs = new BufferedStream(fs))
               using (StreamReader sr = new StreamReader(bs))
               {
                   string line;
                   while ((line = sr.ReadLine()) != null)
                   {
                       var pointString = line.Split(new char[] { ' ' });
                       points.Add(new Point()
                       {
                           X = float.Parse(pointString[0]),
                           Y = float.Parse(pointString[1]),
                           Z = float.Parse(pointString[2]),
                       });
                   }
               }

               return points.ToArray();
           }

           static Tuple<bool[], Point[]> GetMissingPoints(Coordinates coordinates)
           {
               List<Point> points = new List<Point>();
               var missingPoints = new bool[] { false, false };

               var rowMissingPoints = rowPatchMissingPoints
                   .FirstOrDefault(mp => mp.Key.Lon == coordinates.Lon && mp.Key.Lat == coordinates.Lat + patchStep);
               if (!rowMissingPoints.Equals(default(KeyValuePair<Coordinates, Point[]>)))
               {
                   var rowPoints = rowMissingPoints.Value.Where(p => rowMissingPoints.Key.Lat * 1000 == p.Y);
                   points.AddRange(rowPoints);
                   rowPatchMissingPoints.Remove(rowMissingPoints.Key);
                   missingPoints[0] = true;
                   Debug.Log("Top: " + rowMissingPoints.Key.Lon + "_" + rowMissingPoints.Key.Lat + " " + points.Count());
               }

               var columnMissingPoints = columnPatchMissingPoints
                   .FirstOrDefault(mp => mp.Key.Lon == coordinates.Lon + patchStep && mp.Key.Lat == coordinates.Lat);
               if (!columnMissingPoints.Equals(default(KeyValuePair<Coordinates, Point[]>)))
               {
                   var columnPoints = columnMissingPoints.Value.Where(p => columnMissingPoints.Key.Lon * 1000 == p.X);
                   points.AddRange(columnPoints);
                   columnPatchMissingPoints.Remove(columnMissingPoints.Key);
                   missingPoints[1] = true;
                   Debug.Log("Right: " + columnMissingPoints.Key.Lon + "_" + columnMissingPoints.Key.Lat + " " + points.Count());
               }

               if (missingPoints.All(mp => mp == true))
               {
                   var angleMissingPoint = anglePatchMissingPoints
                       .FirstOrDefault(mp => mp.Key.Lon == coordinates.Lon + patchStep && mp.Key.Lat == coordinates.Lat + patchStep);
                   if (!angleMissingPoint.Equals(default(KeyValuePair<Coordinates, Point[]>)))
                   {
                       var anglePoint = angleMissingPoint.Value;
                       points.Add(anglePoint);
                       anglePatchMissingPoints.Remove(angleMissingPoint.Key);
                   }
                   Debug.Log("TopRight: " + angleMissingPoint.Key.Lon + "_" + angleMissingPoint.Key.Lat);
               }

               Debug.Log(points.Count);
               return new Tuple<bool[], Point[]>(missingPoints, points.ToArray());
           }
           #endregion
      */
    }

    struct Patch
    {
        public int Lon;
        public int Lat;
        public Tile[] Tiles;
    }

    struct Tile
    {
        public int Lon;
        public int Lat;
        public MeshFilter MeshFilter;
    }
}
