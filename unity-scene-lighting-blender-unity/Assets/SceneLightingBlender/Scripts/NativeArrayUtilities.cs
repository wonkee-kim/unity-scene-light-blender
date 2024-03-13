using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace GraphicTools
{
    public class NativeArrayUtilities : MonoBehaviour
    {
        // https://gist.github.com/LotteMakesStuff/c2f9b764b15f74d14c00ceb4214356b4
        public static unsafe NativeArray<float3> GetNativeArrays(Vector3[] array, Allocator allocator, NativeArrayOptions nativeArrayOptions)
        {
            // create a destination NativeArray to hold the vertices
            NativeArray<float3> nativeArray = new NativeArray<float3>(array.Length, allocator, nativeArrayOptions);

            // pin the mesh's vertex buffer in place...
            fixed (void* arrayBufferPointer = array)
            {
                // ...and use memcpy to copy the Vector3[] into a NativeArray<floar3> without casting. whould be fast!
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(nativeArray),
                    arrayBufferPointer, array.Length * (long)UnsafeUtility.SizeOf<float3>());
            }
            // we only hve to fix the .net array in place, the NativeArray is allocated in the C++ side of the engine and
            // wont move arround unexpectedly. We have a pointer to it not a reference! thats basically what fixed does,
            // we create a scope where its 'safe' to get a pointer and directly manipulate the array

            return nativeArray;
        }

        public static unsafe NativeArray<float2> GetNativeArrays(Vector2[] array, Allocator allocator, NativeArrayOptions nativeArrayOptions)
        {
            NativeArray<float2> nativeArray = new NativeArray<float2>(array.Length, allocator, nativeArrayOptions);

            fixed (void* arrayBufferPointer = array)
            {
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(nativeArray),
                    arrayBufferPointer, array.Length * (long)UnsafeUtility.SizeOf<float2>());
            }

            return nativeArray;
        }

        public static unsafe NativeArray<float> GetNativeArrays(float[] array, Allocator allocator, NativeArrayOptions nativeArrayOptions)
        {
            NativeArray<float> nativeArray = new NativeArray<float>(array.Length, allocator, nativeArrayOptions);

            fixed (void* arrayBufferPointer = array)
            {
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(nativeArray),
                    arrayBufferPointer, array.Length * (long)UnsafeUtility.SizeOf<float>());
            }

            return nativeArray;
        }

        public static unsafe NativeArray<SphericalHarmonicsL2> GetNativeArrays(SphericalHarmonicsL2[] array, Allocator allocator, NativeArrayOptions nativeArrayOptions)
        {
            NativeArray<SphericalHarmonicsL2> nativeArray = new NativeArray<SphericalHarmonicsL2>(array.Length, allocator, nativeArrayOptions);

            fixed (void* arrayBufferPointer = array)
            {
                UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(nativeArray),
                    arrayBufferPointer, array.Length * (long)UnsafeUtility.SizeOf<SphericalHarmonicsL2>());
            }

            return nativeArray;
        }

        public static unsafe void GetArrayFromNativeArray(Vector3[] vertexArray, NativeArray<float3> vertexBuffer)
        {
            // pin the target vertex array and get a pointer to it
            fixed (void* vertexArrayPointer = vertexArray)
            {
                // memcopy the native array over the top
                UnsafeUtility.MemCpy(vertexArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(vertexBuffer), vertexArray.Length * (long)UnsafeUtility.SizeOf<float3>());
            }
        }

        public static unsafe void GetArrayFromNativeArray(Vector2[] array, NativeArray<float2> arrayBuffer)
        {
            fixed (void* vertexArrayPointer = array)
            {
                UnsafeUtility.MemCpy(vertexArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(arrayBuffer), array.Length * (long)UnsafeUtility.SizeOf<float2>());
            }
        }

        public static unsafe void GetArrayFromNativeArray(int[] array, NativeArray<int> arrayBuffer)
        {
            fixed (void* vertexArrayPointer = array)
            {
                UnsafeUtility.MemCpy(vertexArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(arrayBuffer), array.Length * (long)UnsafeUtility.SizeOf<int>());
            }
        }

        public static unsafe void GetArrayFromNativeArray(Color32[] array, NativeArray<Color32> arrayBuffer)
        {
            fixed (void* vertexArrayPointer = array)
            {
                UnsafeUtility.MemCpy(vertexArrayPointer, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(arrayBuffer), array.Length * (long)UnsafeUtility.SizeOf<Color32>());
            }
        }

        public static unsafe void GetArrayFromNativeArray(SphericalHarmonicsL2[] dst, NativeArray<SphericalHarmonicsL2> src)
        {
            int srcSize = src.Length * UnsafeUtility.SizeOf<SphericalHarmonicsL2>();
            int dstSize = dst.Length * UnsafeUtility.SizeOf<SphericalHarmonicsL2>();
            void* srcPtr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src);
            void* dstPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(dst, out ulong handle);
            UnsafeUtility.MemCpy(destination: dstPtr, source: srcPtr, size: srcSize);
            UnsafeUtility.ReleaseGCObject(handle);
        }
    }
}
