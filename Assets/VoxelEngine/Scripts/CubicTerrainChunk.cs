﻿using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Threading;

/// <summary>
/// Cubic terrain chunk.
/// </summary>
public class CubicTerrainChunk : MonoBehaviour
{
	#region Static data
	private static Vector3[] leftSideVertices = new Vector3[]
	{
		new Vector3(0,0,1),
		new Vector3(0,0,0),
		new Vector3(0,1,0),
		new Vector3(0,1,1)
	};
	
	private static int[] leftSideIndices = new int[]
	{
		1,0,2,0,3,2
	};
	
	private static Vector3[] rightSideVertices = new Vector3[]
	{
		new Vector3(1,0,0),
		new Vector3(1,0,1),
		new Vector3(1,1,1),
		new Vector3(1,1,0),
	};
	
	private static int[] rightSideIndices = new int[]
	{
		1,0,2,0,3,2
	};
	
	private static Vector3[] topSideVertices = new Vector3[]
	{
		new Vector3(0,1,0),
		new Vector3(1,1,0),
		new Vector3(1,1,1),
		new Vector3(0,1,1),
	};
	
	private static int[] topSideIndices = new int[]
	{
		1,0,2,0,3,2
	};
	
	private static Vector3[] bottomSideVertices = new Vector3[]
	{
		new Vector3(0,0,0),
		new Vector3(1,0,0),
		new Vector3(1,0,1),
		new Vector3(0,0,1)
	};
	
	private static int[] bottomSideIndices = new int[]
	{
		1,2,0,2,3,0
	};
	
	private static Vector3[] backSideVertices = new Vector3[]
	{
		new Vector3(0,0,0),
		new Vector3(1,0,0),
		new Vector3(1,1,0),
		new Vector3(0,1,0)
	};
	
	private static int[] backSideIndices = new int[]
	{
		2,1,0,0,3,2
	};
	
	private static Vector3[] frontSideVertices = new Vector3[]
	{
		new Vector3(1,0,1),
		new Vector3(0,0,1),
		new Vector3(0,1,1),
		new Vector3(1,1,1)
	};
	
	private static int[] frontSideIndices = new int[]
	{
		2,1,0,0,3,2
	};
	
	#endregion

	#region Helper classes and functions

	// Mesh data class.
	// Used for sending mesh data from generation thread to the main thread.
	class MeshData
	{
		public Vector3[] vertices;
		public int[] triangles;
		public Color[] colors;
		public Vector2[] uvs;
		public Dictionary<int, TriangleBlockInfo> triangleLookupTable;
	}

	// Triangle info.
	// Will identify triangle indices with
	class TriangleBlockInfo
	{
		public int x, y, z;

		public TriangleBlockInfo(int x, int y, int z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}
	}
	#endregion

	/// <summary>
	/// Gets or sets the chunk data.
	/// </summary>
	/// <value>The chunk data.</value>
	public CubicTerrainData chunkData
	{
		get { return this._chunkData; }
		set { this._chunkData = value; this._isDirty = true; }
	}

	public CubicTerrain master;

	/// <summary>
	/// The frame where an update occured
	/// </summary>
	private static float lastUpdateFrame;

	/// <summary>
	/// The chunk data.
	/// </summary>
	private CubicTerrainData _chunkData;

	/// <summary>
	/// If this flag is set to true the chunk data will get rebuilt.
	/// </summary>
	private bool _isDirty;

	public bool isDirty
	{
		get { return this._isDirty || (this.chunkData == null || this.chunkData.isDirty); }
		set { this._isDirty = value; if (this.chunkData != null) { this.chunkData.isDirty = value; } }
	}

	/// <summary>
	/// The terrain material.
	/// </summary>
	public Material terrainMaterial;

	/// <summary>
	/// The renderer.
	/// </summary>
	private MeshRenderer renderer;
	private MeshFilter filter;
	private MeshCollider meshCollider;

	/// <summary>
	/// The new mesh.
	/// Generated from another thread. asnychronously!
	/// </summary>
	private MeshData newMeshData;

	private object meshDataLockObject = new object();

	private Thread meshGenerationThread;

	private Dictionary<int, TriangleBlockInfo> triangleLookupTable;

	public void Start()
	{
		this.filter = this.gameObject.AddComponent<MeshFilter> ();
		this.renderer = this.gameObject.AddComponent<MeshRenderer> ();
		this.meshCollider = this.gameObject.AddComponent<MeshCollider> ();
		this.renderer.sharedMaterial = this.terrainMaterial;
	}

	/// <summary>
	/// Updates the chunk.
	/// </summary>
	public void FixedUpdate()
	{
		if (this.isDirty)
		{
			this.meshGenerationThread = new Thread(this.GenerateMesh);
			this.meshGenerationThread.Start ();

			this.isDirty = false;
		}

		if (lastUpdateFrame < Time.frameCount - 5)
		{
			// Lag protection
			lock (this.meshDataLockObject)
			{
				if (this.newMeshData != null)
				{
					// Generate new mesh object from raw data.
					Mesh newMesh = new Mesh();
					newMesh.vertices = this.newMeshData.vertices;
					newMesh.colors = this.newMeshData.colors;
					newMesh.uv = this.newMeshData.uvs;
					newMesh.triangles = this.newMeshData.triangles;
					newMesh.RecalculateBounds ();
					newMesh.RecalculateNormals ();
					newMesh.Optimize();

					// Set lookup table
					this.triangleLookupTable = this.newMeshData.triangleLookupTable;

					// Set new mesh to the filter
					this.filter.mesh = newMesh;
					this.meshCollider.sharedMesh = newMesh;

					// Cleanup
					this.newMeshData = null;
					this.meshGenerationThread.Abort();
					this.meshGenerationThread = null;

					lastUpdateFrame = Time.frameCount;
				}
			}
		}
	}

	/// <summary>
	/// Converts the given triangle index (for example obtained by raycasting) to the block coordinates.
	/// 
	/// </summary>
	/// <returns>The index to block.</returns>
	/// <param name="triangleIndex">Triangle index.</param>
	public Vector3 triangleIndexToBlock(int triangleIndex)
	{
		TriangleBlockInfo blockInfo = this.triangleLookupTable [triangleIndex*3];
		return new Vector3 (blockInfo.x, blockInfo.y, blockInfo.z);
	}

	/// <summary>
	/// Generates the mesh from _chunkData.
	/// </summary>
	/// <returns>The mesh.</returns>
	private void GenerateMesh()
	{
		CubicTerrainData.VoxelData[][][] voxelData = this._chunkData.voxelData;
		int indicesCounter = 0;
		
		List<Vector3> vertices = new List<Vector3> ();
		List<int> indices = new List<int> ();
		List<Vector2> uvs = new List<Vector2> ();
		List<Color> colors = new List<Color> ();

		Dictionary<int, TriangleBlockInfo> triangleLookupTable = new Dictionary<int, TriangleBlockInfo> ();

		// Determine block visibilities
		for (int x = 0; x < this._chunkData.width; x++)
		{
			for (int y = 0; y < this._chunkData.height; y++)
			{
				for (int z = 0; z < this._chunkData.depth; z++)
				{
					// Voxel in my position?
					if (voxelData[x][y][z] == null || voxelData[x][y][z].blockId < 0)
						continue;

					// Left side un-covered?
					if (x == 0 || (voxelData[x-1][y][z] == null || voxelData[x-1][y][z].blockId < 0))
					{
						// Un-Covered! Add mesh data!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, leftSideVertices, leftSideIndices, indicesCounter,x,y,z, Color.blue, Blocks.GetBlock(voxelData[x][y][z].blockId).leftUv);
						indicesCounter += leftSideVertices.Length;
					}
					// Right side un-covered?
					if (x == this._chunkData.width -1 || ((voxelData[x+1][y][z] == null || voxelData[x+1][y][z].blockId < 0)))
					{
						// Un-Covered!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, rightSideVertices, rightSideIndices, indicesCounter,x,y,z, Color.black, Blocks.GetBlock(voxelData[x][y][z].blockId).rightUv);
						indicesCounter += rightSideVertices.Length;
					}
					// Top side un-covered?
					if (y == this._chunkData.height-1 || ((voxelData[x][y+1][z] == null || voxelData[x][y+1][z].blockId < 0)))
					{
						// Un-Covered!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, topSideVertices, topSideIndices, indicesCounter,x,y,z, Color.gray, Blocks.GetBlock(voxelData[x][y][z].blockId).topUv); // Blocks.GetBlock(voxelData[x][y][z].blockId).topUv);
						indicesCounter += topSideVertices.Length;
					}
					// Bottom side un-covered?
					if (y == 0 || (voxelData[x][y-1][z] == null || voxelData[x][y-1][z].blockId < 0))
					{
						// Un-Covered!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, bottomSideVertices, bottomSideIndices, indicesCounter,x,y,z, Color.green, Blocks.GetBlock(voxelData[x][y][z].blockId).bottomUv);
						indicesCounter += bottomSideVertices.Length;
					}
					// Back side un-covered?
					if (z == 0 || (voxelData[x][y][z-1] == null || voxelData[x][y][z-1].blockId < 0))
					{
						// Un-Covered!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, backSideVertices, backSideIndices, indicesCounter,x,y,z, Color.yellow, Blocks.GetBlock(voxelData[x][y][z].blockId).backUv);
						indicesCounter += backSideVertices.Length;
                    }
                    // Front side un-covered?
					if (z == this._chunkData.depth-1 || ((voxelData[x][y][z+1] == null || voxelData[x][y][z+1].blockId < 0)))
					{
						// Un-Covered!
						WriteSideData(vertices, indices, uvs, colors, triangleLookupTable, frontSideVertices, frontSideIndices, indicesCounter,x,y,z, Color.red, Blocks.GetBlock(voxelData[x][y][z].blockId).frontUv);
						indicesCounter += frontSideVertices.Length;
					}
				}
			}
		}

		// Set mesh data
		lock (this.meshDataLockObject)
		{
			this.newMeshData = new MeshData ();
			this.newMeshData.vertices = vertices.ToArray ();
			this.newMeshData.triangles = indices.ToArray ();
			this.newMeshData.uvs = uvs.ToArray ();
			this.newMeshData.colors = colors.ToArray ();
			this.newMeshData.triangleLookupTable = triangleLookupTable;
		}
	}

	/// <summary>
	/// Writes the side data to the given mesh.
	/// </summary>
	/// <param name="mesh">Mesh.</param>
	/// <param name="sideVertices">Side vertices.</param>
	/// <param name="sideIndices">Side indices.</param>
	/// <param name="indiceCounter">Indice counter.</param>
	private static void WriteSideData(List<Vector3> vertices, List<int> indices, List<Vector2> uvs, List<Color> colors, Dictionary<int, TriangleBlockInfo> triangleLookupTable, Vector3[] sideVertices, int[] sideIndices, int indicesCounter, int x, int y, int z, Color color, Vector2[] uv)
	{
		// 4 vertices per face, so divide indicesCounter which is the current vertex by 4.
		int faceCount = indicesCounter / 4;

		TriangleBlockInfo blockInfo = new TriangleBlockInfo (x, y, z);

		// Add indices to the update list
		triangleLookupTable.Add((faceCount*6), blockInfo);
		triangleLookupTable.Add((faceCount*6)+1, blockInfo);
		triangleLookupTable.Add((faceCount*6)+2, blockInfo);
		triangleLookupTable.Add((faceCount*6)+3, blockInfo);
		triangleLookupTable.Add((faceCount*6)+4, blockInfo);
		triangleLookupTable.Add((faceCount*6)+5, blockInfo);

		// Calculate absolute vertex index count.
		int[] absoluteIndices = new int[sideIndices.Length];
		for (int i = 0; i < sideIndices.Length; i++)
		{
			absoluteIndices[i] = indicesCounter+sideIndices[i];
		}

		// Transform vertices based on the block's position.
		Vector3[] absoluteVertices = new Vector3[sideVertices.Length];
		for (int i = 0; i < sideVertices.Length; i++)
		{
			absoluteVertices[i] = sideVertices[i];
			absoluteVertices[i].x += (float) x;
			absoluteVertices[i].y += (float) y;
			absoluteVertices[i].z += (float) z;
			colors.Add (color);
		}

		// Add mesh data to the lists.
		vertices.AddRange (absoluteVertices);
		indices.AddRange (absoluteIndices);
		uvs.AddRange (uv);
	}
}
