﻿using Game.Behaviours.ECS.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Game.Behaviours.ECS.Systems
{
    public class GrassSystem : SystemBase
    {
        public Entity GrassTemplateEntity { get; set; }
        public Transform PlayerTransform { get; set; }
        
        protected override void OnUpdate()
        {
            float3 playerLoc = PlayerTransform.position;
            playerLoc.y = 0f;
            
            float3 playerRight = PlayerTransform.right;
            float3 playerForward = PlayerTransform.forward;
            
            var query = new EntityQueryDesc{
                All = new ComponentType[] {typeof(GrassComponent), typeof(Translation),  typeof(Rotation), typeof(GrassData)},
                Options = EntityQueryOptions.IncludeDisabled
            };
            
            var entityQuery = GetEntityQuery(query);
            var AABBQuery = GetEntityQuery(typeof(AABB));
            var sphereQuery = GetEntityQuery(typeof(Sphere));
            
            var translations = entityQuery.ToComponentDataArray<Translation>(Allocator.TempJob);
            var grassData = entityQuery.ToComponentDataArray<GrassData>(Allocator.TempJob);
            var aabbColliders = AABBQuery.ToComponentDataArray<AABB>(Allocator.TempJob);
            var sphereColliders = sphereQuery.ToComponentDataArray<Sphere>(Allocator.TempJob);
            
            var rotations = new NativeArray<Rotation>(translations.Length, Allocator.TempJob);
            var collisionResults = new NativeArray<bool>(translations.Length, Allocator.TempJob);
            var nextPositions = new NativeArray<float3>(translations.Length, Allocator.TempJob);

            var distanceCheckJob = new DistanceCheckJob
            {
                PlayerLoc = playerLoc,
                PlayerForward = playerForward,
                PlayerRight = playerRight,
                Translations = translations,
                NextPositions = nextPositions,
                Rotations = rotations,
                GrassData = grassData,
            };
            
            var distanceCheckHandle = distanceCheckJob.Schedule(translations.Length, 32);
            distanceCheckHandle.Complete();
            
            var collisionCheckJob = new AABBCollisionJob
            {
                AABBColliders = aabbColliders,
                SphereColliders = sphereColliders,
                NextPositions = nextPositions,
                CollisionResult = collisionResults,
            };
            
            var collisionCheckHandle = collisionCheckJob.Schedule(translations.Length, 32);
            collisionCheckHandle.Complete();

            var entities = entityQuery.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < collisionResults.Length; i++)
            {
                var translation = new Translation
                {
                    Value = nextPositions[i]
                };
                    
                EntityManager.SetComponentData(entities[i], rotations[i]);

                if (!grassData[i].IsDynamic)
                {
                    continue;
                }
                
                EntityManager.SetComponentData(entities[i], translation);

                if (!collisionResults[i])
                {
                    EntityManager.RemoveComponent<Disabled>(entities[i]);
                }
                else
                {
                    EntityManager.AddComponent<Disabled>(entities[i]);
                }
            }
            
            entities.Dispose();
            translations.Dispose();
            aabbColliders.Dispose();
            sphereColliders.Dispose();
            grassData.Dispose();
            rotations.Dispose();
            collisionResults.Dispose();
            nextPositions.Dispose();
        }

        private struct DistanceCheckJob : IJobParallelFor
        {
            [ReadOnly] public float3 PlayerLoc;
            [ReadOnly] public float3 PlayerForward;
            [ReadOnly] public float3 PlayerRight;
            [ReadOnly] public NativeArray<Translation> Translations;
            public NativeArray<float3> NextPositions;
            public NativeArray<Rotation> Rotations;
            public NativeArray<GrassData> GrassData;
            
            public void Execute(int i)
            {
                var direction = PlayerLoc - Translations[i].Value;
                var distance = math.lengthsq(direction);

                if (GrassData[i].IsDynamic)
                {
                    if (distance > Constants.MaxDistSq)
                    {
                        NextPositions[i] = PlayerLoc + math.normalize(direction) * Constants.MaxDist;
                    }
                    else
                    {
                        NextPositions[i] = Translations[i].Value;
                    }
                }

                if (distance < Constants.GrassCloseThresholdSq)
                {
                    var rotation = new Rotation
                    {
                        Value = quaternion.AxisAngle(PlayerForward,
                            (1f - distance / Constants.GrassCloseThresholdSq) * (math.PI / 6f) *
                            (math.dot(PlayerRight, -direction) > 0 ? -1f : 1f)),
                    };

                    Rotations[i] = rotation;
                }
                else
                {
                    var rotation = new Rotation
                    {
                        Value = quaternion.identity
                    };

                    Rotations[i] = rotation;
                }
            }
        }
        
        [BurstCompile]
        private struct AABBCollisionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<AABB> AABBColliders;
            [ReadOnly] public NativeArray<Sphere> SphereColliders;
            [ReadOnly] public NativeArray<float3> NextPositions;
            public NativeArray<bool> CollisionResult;

            public void Execute(int i)
            {
                for (int j = 0; j < AABBColliders.Length; j++)
                {
                    if (ECSPhysics.Intersect(NextPositions[i], AABBColliders[j]))
                    {
                        CollisionResult[i] = true;
                    }
                }
                
                for (int j = 0; j < SphereColliders.Length; j++)
                {
                    if (ECSPhysics.Intersect(NextPositions[i], SphereColliders[j]))
                    {
                        CollisionResult[i] = true;
                    }
                }
            }
        }
    }

    public struct GrassData : IComponentData
    {
        public bool IsDynamic;
        public float SwayDuration;
    }
}
