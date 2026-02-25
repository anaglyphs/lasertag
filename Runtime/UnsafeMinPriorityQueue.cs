using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    /// <summary>
    ///  Represents a min priority queue.
    /// </summary>
    /// <typeparam name="T">Specifies the type of elements in the queue.</typeparam>
    /// <remarks>
    ///  https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs
    /// </remarks>
    unsafe struct UnsafeMinPriorityQueue<T> : INativeDisposable
        where T : unmanaged, IComparable<T>
    {
        internal UnsafeList<T> nodes;
        const int Arity = 4;
        const int Log2Arity = 2;
        public readonly int Count => nodes.Length;
        public UnsafeMinPriorityQueue(int initialCapacity, AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            nodes = new(initialCapacity, allocator, options);
        }
        public static UnsafeMinPriorityQueue<T>* Create(int initialCapacity, AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            var queue = AllocatorManager.Allocate<UnsafeMinPriorityQueue<T>>(allocator);
            *queue = new(initialCapacity, allocator, options);
            return queue;
        }
        public static void Destroy(UnsafeMinPriorityQueue<T>* queue)
        {
            CheckNull(queue);
            var allocator = queue->nodes.Allocator;
            queue->Dispose();
            AllocatorManager.Free(allocator, queue);
        }



        /// <summary>
        /// Convert existing <see cref="NativeList{T}"/> to <see cref="UnsafeMinPriorityQueue{T}"/>. modifying <paramref name="list"/> after call this method will be resulted in unexpected behaviour.
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public static UnsafeMinPriorityQueue<T>* ConvertFromExistingList(UnsafeList<T>* list)
        {
            var queue = (UnsafeMinPriorityQueue<T>*)list;
            queue->Heapify();
            return queue;
        }
        /// <summary>
        ///  Adds the specified element with associated priority to the <see cref="UnsafeMinPriorityQueue{T}"/>.
        /// </summary>
        /// <param name="element">The element to add to the <see cref="UnsafeMinPriorityQueue{T}"/></param>
        public void Enqueue(T element)
        {
            // Virtually add the node at the end of the underlying array.
            // Note that the node being enqueued does not need to be physically placed
            // there at this point, as such an assignment would be redundant.
            var currentSize = nodes.Length;
            nodes.Length = currentSize + 1;

            MoveUp(element, currentSize);
        }
        /// <summary>
        ///  Returns the minimal element from the <see cref="UnsafeMinPriorityQueue{T}"/> without removing it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The <see cref="UnsafeMinPriorityQueue{T}"/> is empty.</exception>
        /// <returns>The minimal element of the <see cref="UnsafeMinPriorityQueue{T}"/>.</returns>
        public T Peek()
        {
            return nodes[0];
        }
        /// <summary>
        ///  Removes and returns the minimal element from the <see cref="UnsafeMinPriorityQueue{T}"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The queue is empty.</exception>
        /// <returns>The minimal element of the <see cref="UnsafeMinPriorityQueue{T}"/>.</returns>
        public T Dequeue()
        {
            var element = nodes[0];
            RemoveRootNode();
            return element;
        }
        /// <summary>
        ///  Removes the minimal element from the <see cref="UnsafeMinPriorityQueue{T}"/>,
        ///  and copies it to the <paramref name="element"/> parameter.
        /// </summary>
        /// <param name="element">The removed element.</param>
        /// <returns>
        ///  <see langword="true"/> if the element is successfully removed;
        ///  <see langword="false"/> if the <see cref="UnsafeMinPriorityQueue{T}"/> is empty.
        /// </returns>
        public bool TryDequeue([MaybeNullWhen(false)] out T element)
        {
            if (!nodes.IsEmpty)
            {
                element = nodes[0];
                RemoveRootNode();
                return true;
            }

            element = default;
            return false;
        }
        /// <summary>
        ///  Returns a value that indicates whether there is a minimal element in the <see cref="UnsafeMinPriorityQueue{T}"/>,
        ///  and if one is present, copies it to the <paramref name="element"/> parameter.
        ///  The element is not removed from the <see cref="UnsafeMinPriorityQueue{T}"/>.
        /// </summary>
        /// <param name="element">The minimal element in the queue.</param>
        /// <returns>
        ///  <see langword="true"/> if there is a minimal element;
        ///  <see langword="false"/> if the <see cref="UnsafeMinPriorityQueue{T}"/> is empty.
        /// </returns>
        public bool TryPeek([MaybeNullWhen(false)] out T element)
        {
            if (!nodes.IsEmpty)
            {
                element = nodes[0];
                return true;
            }

            element = default;
            return false;
        }

        public void EnqueueRange(ReadOnlySpan<T> elements)
        {
            int newCount = Count + elements.Length;
            if (nodes.Capacity < newCount)
            {
                nodes.SetCapacity(newCount);
            }

            if (nodes.IsEmpty)
            {
                fixed (T* ptr = elements)
                {
                    nodes.AddRangeNoResize(ptr, elements.Length);
                }
                Heapify();
            }
            else
            {
                foreach (var element in elements)
                {
                    Enqueue(element);
                }
            }


        }

        public void Clear() => nodes.Clear();
        /// <summary>
        /// Removes the node from the root of the heap
        /// </summary>
        private void RemoveRootNode()
        {
            int lastNodeIndex = nodes.Length - 1;

            if (lastNodeIndex > 0)
            {
                var lastNode = nodes[lastNodeIndex];
                MoveDown(lastNode, 0);
            }
            nodes.Length -= 1;
        }
        /// <summary>
        /// Gets the index of an element's parent.
        /// </summary>
        [return: AssumeRange(0, (int.MaxValue - 1) >> Log2Arity)]
        private static int GetParentIndex([AssumeRange(1, int.MaxValue)] int index) => (index - 1) >> Log2Arity;

        /// <summary>
        /// Gets the index of the first child of an element.
        /// </summary>
        [return: AssumeRange(1, int.MaxValue)]
        private static int GetFirstChildIndex(int index) => (index << Log2Arity) + 1;
        /// <summary>
        /// Converts an unordered list into a heap.
        /// </summary>
        internal void Heapify()
        {
            if (nodes.Length <= 1)
            {
                return;
            }

            // Leaves of the tree are in fact 1-element heaps, for which there
            // is no need to correct them. The heap property needs to be restored
            // only for higher nodes, starting from the first node that has children.
            // It is the parent of the very last element in the array.

            int lastParentWithChildren = GetParentIndex(nodes.Length - 1);

            for (int index = lastParentWithChildren; index >= 0; --index)
            {
                MoveDown(nodes[index], index);
            }
        }
        /// <summary>
        /// Moves a node up in the tree to restore heap order.
        /// </summary>
        void MoveUp(T node, int nodeIndex)
        {
            while (nodeIndex > 0)
            {
                var parentIndex = GetParentIndex(nodeIndex);
                var parent = nodes[parentIndex];
                if (node.CompareTo(parent) < 0)
                {
                    nodes[nodeIndex] = parent;
                    nodeIndex = parentIndex;
                }
                else
                {
                    break;
                }
            }
            nodes[nodeIndex] = node;
        }
        /// <summary>
        /// Moves a node down in the tree to restore heap order.
        /// </summary>
        void MoveDown(T node, int nodeIndex)
        {
            int i;
            while ((i = GetFirstChildIndex(nodeIndex)) < nodes.Length)
            {
                // Find the child node with the minimal priority
                var minChild = nodes[i];
                var minChildIndex = i;
                var childIndexUpperBound = math.min(i + Arity, nodes.Length);
                while (++i < childIndexUpperBound)
                {
                    var nextChild = nodes[i];
                    if (nextChild.CompareTo(minChild) < 0)
                    {
                        minChild = nextChild;
                        minChildIndex = i;
                    }
                }
                // Heap property is satisfied; insert node in this location.
                if (node.CompareTo(minChild) <= 0)
                {
                    break;
                }
                // Move the minimal child up by one node and
                // continue recursively from its location.
                nodes[nodeIndex] = minChild;
                nodeIndex = minChildIndex;
            }
            nodes[nodeIndex] = node;
        }

        public JobHandle Dispose(JobHandle inputDeps) => nodes.Dispose(inputDeps);

        public void Dispose() => nodes.Dispose();

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void CheckNull(void* queue)
        {
            if (queue == null)
            {
                throw new InvalidOperationException("UnsafeMinPriorityQueue has yet to be created or has been destroyed!");
            }
        }
    }
}


