﻿using System;
using System.Collections.Generic;
using GamePath;
using UnityEngine;

public class MoveHelperIconic : EntityMoveHelper
{
    private struct DestroyData
    {
        public int offsetX;

        public int offsetZ;

        public int stepX;

        public int stepZ;

        public DestroyData(int _offsetX, int _offsetZ, int _stepX, int _stepZ)
        {
            offsetX = _offsetX;
            offsetZ = _offsetZ;
            stepX = _stepX;
            stepZ = _stepZ;
        }
    }

    private const float cDoneXZDistSq = 0.0004f;

    private const float cCheckBlockedDist = 0.35f;

    private const float cCheckBlockedRadius = 0.125f;

    private const float cCheckSidestepDist = 0.35f;

    private const float cCheckSidestepRadius = 0.1f;

    private const float cTempMoveDist = 0.4f;

    private const float cYawNextDist = 1.5f;

    private const float cMoveDirectDist = 0.65f;

    private const float cMoveSlowDist = 0.6f;

    private const float cDigXZDistSq = 0.0100000007f;

    private const float cDigDiagonalXZDistSq = 2.25f;

    private const float cDigAngleCos = 0.86f;

    private const float cJumpUpXZDistSq = 0.0400000028f;

    private const float cLadderXZDistSq = 0.108900011f;

    private const int cDestroyRadius = 11;

    private const float cUnreachJumpMin = 1.2f;

    private const int cCollisionMask = 1082195968;

    public Vector3 JumpToPos;

    public bool IsActive;

    private bool canBreakBlocks;

    public bool IsBlocked;

    public float BlockedTime;

    public EntityAlive BlockedEntity;

    public WorldRayHitInfo HitInfo;

    public float DamageScale;

    public bool IsUnreachableAbove;

    public bool IsUnreachableSide;

    public bool IsUnreachableSideJump;

    public Vector3 UnreachablePos;

    public float SideStepAngle;

    public float UnreachablePercent;

    public bool IsDestroyAreaTryUnreachable;

    public bool IsDestroyArea;

    private DamageResponse damageResponse = DamageResponse.New(_fatal: false);

    private EntityAliveIconic entity;

    private GameRandom random;

    private Vector3 moveToPos;

    private float moveToDistance;

    private int moveToTicks;

    private int moveToFailCnt;

    private float moveToDir;

    private Vector3 focusPos;

    private int focusTicks;

    private int obstacleCheckTickDelay;

    private bool hasNextPos;

    private Vector3 nextMoveToPos;

    private Vector3 tempMoveToPos;

    private bool isTempMove;

    private float blockedHeight;

    private float blockedDistSq;

    private float blockedEntityDistSq;

    private bool isDigging;

    private float moveSpeed;

    private int expiryTicks;

    private bool isClimb;

    private float jumpYaw;

    private int swimStrokeDelayTicks;

    private float ccRadius;

    private float ccHeight;

    private static float[] checkEdgeXs = new float[3] { 0f, -0.25f, 0.25f };

    private const float cDigMovedDist = 0.5f;

    private Vector3 digStartPos;

    private float digForTicks;

    private float digTicks;

    private float digActionTicks;

    private bool digAttacked;

    private float digForwardCount;

    private static DestroyData[] destroyData = new DestroyData[7]
    {
        new DestroyData(-1, 1, 1, 0),
        new DestroyData(1, 1, 0, -1),
        new DestroyData(1, -1, -1, 0),
        new DestroyData(-1, -1, 0, 1),
        new DestroyData(-1, 1, 1, 0),
        new DestroyData(1, 1, 0, -1),
        new DestroyData(1, -1, -1, 0)
    };

    private static int[] blockOpenOffsets = new int[8] { -1, 0, 1, 0, 0, 1, 0, -1 };

    public MoveHelperIconic(EntityAliveIconic _entity) : base(_entity)
    {
        entity = _entity;
        random = _entity.rand;
        moveToPos = _entity.position;
    }


    public new void SetMoveTo(Vector3 _pos, bool _canBreakBlocks)
    {
        Log.Out("MoveHelperIconic - SetMoveToPOS");
        moveToPos = _pos;
        moveSpeed = entity.GetMoveSpeedAggro();
        focusTicks = 0;
        isTempMove = false;
        CanBreakBlocks = _canBreakBlocks;
        isClimb = false;
        IsActive = true;
        expiryTicks = 10;
        ResetStuckCheck();
    }

    public new bool CanBreakBlocks
    {
        get
        {
            Log.Out("MoveHelperIconic - CanBreakBlocks");
            return canBreakBlocks;
        }
        set 
        {

            canBreakBlocks = value; 
        }
    }

    public new void SetMoveTo(PathEntity path, float _speed, bool _canBreakBlocks)
    {
        Log.Out("MoveHelperIconic - SetMoveToPATH");
        PathPoint currentPoint = path.CurrentPoint;
        Vector3 vector = moveToPos;
        moveToPos = currentPoint.AdjustedPositionForEntity(entity);
        CanBreakBlocks = _canBreakBlocks;
        bool flag = true;
        if (IsActive)
        {
            if ((moveToPos - vector).sqrMagnitude < 0.0100000007f)
            {
                flag = false;
            }
        }
        else
        {
            moveToDir = entity.rotation.y;
        }

        if (flag)
        {
            focusTicks = 0;
            isTempMove = false;
            ResetStuckCheck();
        }

        hasNextPos = false;
        PathPoint nextPoint = path.NextPoint;
        if (nextPoint != null)
        {
            hasNextPos = true;
            nextMoveToPos = nextPoint.AdjustedPositionForEntity(entity);
        }

        moveSpeed = _speed;
        isClimb = false;
        expiryTicks = 40;
        IsActive = true;
    }

    public new void Stop()
    {
        StopMove();
        entity.getNavigator().clearPath();
    }

    private void StopMove()
    {
        IsActive = false;
        if (!entity.Jumping || entity.isSwimming)
        {
            entity.SetMoveForward(0f);
            entity.SetRotationAndStopTurning(entity.rotation);
        }

        expiryTicks = 0;
        IsBlocked = false;
        BlockedTime = 0f;
        BlockedEntity = null;
    }

    public new void SetFocusPos(Vector3 _pos)
    {
        focusPos = _pos;
        focusTicks = 5;
    }

    public new void UpdateMoveHelper()
    {
        if (!IsActive)
        {
            return;
        }

        if (--expiryTicks <= 0)
        {
            StopMove();
            return;
        }

        Log.Out("MoveHelperIconic - UpdateMoveHelper");

        ccHeight = entity.m_characterController.GetHeight();
        ccRadius = entity.m_characterController.GetRadius();
        Vector3 position = entity.position;
        Vector3 vector = moveToPos;
        if (isTempMove)
        {
            if (!IsBlocked)
            {
                isTempMove = false;
                ResetStuckCheck();
            }
            else
            {
                vector = tempMoveToPos;
            }
        }

        bool jumping = entity.Jumping;
        bool flag = jumping || entity.isSwimming;
        bool flag2 = jumping && !entity.isSwimming;
        float num = vector.x - position.x;
        float num2 = vector.z - position.z;
        float num3 = num * num + num2 * num2;
        float num4 = vector.y - (position.y + 0.05f);
        bool flag3 = entity.IsInElevator();
        isClimb = false;
        if (flag3 && entity.bCanClimbLadders && num3 < 0.108900011f && num4 > 0.1f && !jumping)
        {
            isClimb = true;
        }
        else if (num3 <= 0.0004f && Utils.FastAbs(num4) < 0.25f && !isTempMove)
        {
            StopMove();
            return;
        }

        AvatarController avatarController = entity.emodel.avatarController;
        if (avatarController.IsRootMotionForced())
        {
            entity.SetMoveForwardWithModifiers(moveSpeed, 1f, _climb: false);
            ResetStuckCheck();
            ClearTempMove();
            ClearBlocked();
            return;
        }

        if ((!flag && !isDigging && !avatarController.IsAnimationWithMotionRunning()) || entity.sleepingOrWakingUp || !entity.bodyDamage.HasLimbs || !entity.bodyDamage.CurrentStun.CanMove() || entity.emodel.IsRagdollActive || avatarController.IsLocomotionPreempted())
        {
            entity.SetMoveForward(0f);
            ResetStuckCheck();
            ClearBlocked();
            return;
        }

        float num5 = moveToPos.x - position.x;
        float num6 = moveToPos.z - position.z;
        float num7 = num5 * num5 + num6 * num6;
        float num8 = moveToPos.y - (position.y + 0.05f);
        if (num8 < -1.1f && num7 <= 0.0100000007f && !flag2 && entity.onGround)
        {
            DigStart(20);
        }

        if (isDigging)
        {
            DigUpdate();
            return;
        }

        float num9 = Mathf.Atan2(num5, num6) * 57.29578f;
        if (flag2)
        {
            moveToDir = num9;
        }
        else
        {
            moveToDir = Mathf.MoveTowardsAngle(moveToDir, num9, 13f);
        }

        entity.emodel.ClearLookAt();
        if (hasNextPos || num7 >= 0.0225f)
        {
            float num10 = 9999f;
            if (flag2)
            {
                num10 = jumpYaw;
            }
            else
            {
                float num11 = num5;
                float num12 = num6;
                if (hasNextPos && num7 <= 2.25f)
                {
                    float t = Mathf.Sqrt(num7) / 1.5f;
                    num11 = Mathf.Lerp(nextMoveToPos.x, moveToPos.x, t) - position.x;
                    num12 = Mathf.Lerp(nextMoveToPos.z, moveToPos.z, t) - position.z;
                }

                if (focusTicks > 0)
                {
                    focusTicks--;
                    num11 = focusPos.x - position.x;
                    num12 = focusPos.z - position.z;
                }

                if (num11 * num11 + num12 * num12 > 0.0001f)
                {
                    num10 = Mathf.Atan2(num11, num12) * 57.29578f;
                }
            }

            if (num10 < 9000f)
            {
                entity.SeekYaw(num10, 0f, 30f);
            }
        }

        float num13 = Mathf.Abs(Mathf.DeltaAngle(num9, moveToDir));
        float num14 = 1f;
        if (IsUnreachableAbove && !entity.IsRunning)
        {
            num14 = 1.3f;
        }

        float num15 = num13 - 15f;
        if (num15 > 0f)
        {
            num14 *= 1f - Utils.FastMin(num15 / 30f, 0.8f);
        }

        if (num14 > 0.5f)
        {
            if (BlockedTime > 0.1f)
            {
                num14 = 0.5f;
            }

            if (focusTicks > 0)
            {
                num14 = 0.45f;
            }
        }

        if (flag3 && !entity.onGround)
        {
            num14 = 0.5f;
        }

        if (entity.hasBeenAttackedTime > 0 && entity.painResistPercent < 1f)
        {
            num14 = 0.1f;
        }

        if (!hasNextPos && !isTempMove && !jumping && num3 < 0.36f && num14 > 0.1f)
        {
            float num16 = num14 * Mathf.Sqrt(num3) / 0.6f;
            if (num16 < 0.1f)
            {
                num16 = 0.1f;
            }

            num14 = num16;
        }

        bool isBreakingBlocks = entity.IsBreakingBlocks;
        if (isBreakingBlocks)
        {
            num14 = 0.03f;
        }

        entity.SetMoveForwardWithModifiers(moveSpeed, num14, isClimb);
        if (num14 > 0f)
        {
            float x = num;
            float z = num2;
            float minMotion = 0.02f * num14;
            float maxMotion = 1f;
            if (!isTempMove)
            {
                if (entity.entityType == EntityType.Bandit)
                {
                    entity.AddMotion(moveToDir, entity.speedForward * num14 * 40f * 0.02f);
                }

                if (SideStepAngle != 0f)
                {
                    float f = (moveToDir + SideStepAngle) * ((float)Math.PI / 180f);
                    x = Mathf.Sin(f);
                    z = Mathf.Cos(f);
                    minMotion = 0.025f;
                    maxMotion = 0.06f;
                    moveToPos = Vector3.MoveTowards(moveToPos, position, 0.0100000007f);
                }
                else if (num3 > 0.422499955f)
                {
                    float f2 = moveToDir * ((float)Math.PI / 180f);
                    x = Mathf.Sin(f2);
                    z = Mathf.Cos(f2);
                }
            }

            entity.MakeMotionMoveToward(x, z, minMotion, maxMotion);
            if (flag3)
            {
                Vector3 normalized = new Vector3(num, num4, num2).normalized;
                float num17 = Mathf.Pow(moveSpeed, 0.4f);
                if (num4 > 0.1f)
                {
                    num17 *= 0.7f;
                }
                else if (num4 < -0.1f)
                {
                    num17 *= 1.4f;
                }

                normalized *= num17 * 0.1f;
                entity.motion = normalized;
            }
        }

        if (flag2)
        {
            return;
        }

        if (entity.isSwimming && entity.swimStrokeRate.x > 0f)
        {
            swimStrokeDelayTicks--;
            if (swimStrokeDelayTicks <= 0)
            {
                swimStrokeDelayTicks = (int)(20f / random.RandomRange(entity.swimStrokeRate.x, entity.swimStrokeRate.y));
                StartSwimStroke();
                swimStrokeDelayTicks += 3;
            }
        }

        if (isBreakingBlocks || num13 > 60f || num14 == 0f)
        {
            moveToTicks = 0;
        }
        else if (++moveToTicks > 6)
        {
            moveToTicks = 0;
            float num18 = Mathf.Sqrt(num * num + num4 * num4 + num2 * num2);
            float num19 = moveToDistance - num18;
            if (num19 < 0.021f)
            {
                if (num19 < -0.01f)
                {
                    moveToDistance = num18;
                }

                if (++moveToFailCnt >= 3)
                {
                    bool flag4 = num8 < -1.1f && num7 <= 0.640000045f;
                    if (flag4 && entity.onGround && random.RandomFloat < 0.6f)
                    {
                        DigStart(80);
                        return;
                    }

                    CheckAreaBlocked();
                    if (IsBlocked)
                    {
                        if (random.RandomFloat < 0.7f)
                        {
                            DamageScale = 6f;
                            obstacleCheckTickDelay = 40;
                        }
                        else
                        {
                            StartJump(calcYaw: false, 0.5f + random.RandomFloat * 0.4f, 1.3f);
                        }
                    }
                    else
                    {
                        if (flag4)
                        {
                            return;
                        }

                        if (random.RandomFloat > 0.5f)
                        {
                            if (entity.Attack(_bAttackReleased: false))
                            {
                                entity.Attack(_bAttackReleased: true);
                            }
                        }
                        else
                        {
                            StartJump(calcYaw: false, 0.7f + random.RandomFloat * 0.8f, 1.4f);
                        }
                    }

                    return;
                }
            }
            else
            {
                moveToDistance = num18;
                if (num19 >= 0.07f)
                {
                    moveToFailCnt = 0;
                }
            }
        }

        if (!entity.onGround && !entity.isSwimming && !flag3 && !isClimb && (num8 < -0.5f || num8 > 0.5f))
        {
            BlockedTime = 0f;
            BlockedEntity = null;
        }
        else if (--obstacleCheckTickDelay <= 0)
        {
            obstacleCheckTickDelay = 4;
            IsBlocked = false;
            BlockedEntity = null;
            blockedDistSq = float.MaxValue;
            if (isClimb)
            {
                CheckBlockedUp(position);
            }
            else if (num13 < 10f)
            {
                CheckEntityBlocked(position, moveToPos);
                CheckWorldBlocked();
                SideStepAngle = 0f;
                if (!IsUnreachableAbove && hasNextPos && (IsBlocked || (bool)BlockedEntity))
                {
                    SideStepAngle = CalcObstacleSideStep();
                    if (SideStepAngle != 0f)
                    {
                        isTempMove = false;
                        BlockedEntity = null;
                        IsBlocked = false;
                    }
                }

                if ((bool)BlockedEntity)
                {
                    if (!IsBlocked || blockedEntityDistSq < blockedDistSq)
                    {
                        moveToTicks = 0;
                        if (random.RandomFloat < 0.1f)
                        {
                            if (BlockedEntity.moveHelper != null && BlockedEntity.moveHelper.IsBlocked)
                            {
                                StartJump(calcYaw: false, 0.7f, BlockedEntity.height * 0.8f);
                            }
                        }
                        else
                        {
                            Push(BlockedEntity);
                        }
                    }
                }
                else if ((IsBlocked || !hasNextPos) && num8 < -1.5f && num7 >= 2.25f && entity.onGround)
                {
                    float num20 = Mathf.Sqrt(num7 + num8 * num8) + 0.001f;
                    if (num8 / num20 < -0.86f)
                    {
                        DigStart(160);
                    }
                }
            }
        }

        if (IsBlocked)
        {
            BlockedTime += 0.05f;
        }
        else
        {
            BlockedTime = 0f;
        }

        if (!entity.CanEntityJump() || isClimb || flag)
        {
            return;
        }

        float num21 = 0f;
        float heightDiff = 0.9f;
        if (BlockedTime > 0.15f && blockedHeight < 1f)
        {
            num21 = 0.55f + random.RandomFloat * 0.3f;
        }
        else if (num8 > 1.5f && num7 <= 0.0400000028f && random.RandomFloat < 0.1f)
        {
            num21 = 0.02f;
        }

        if (IsUnreachableSideJump && num13 < 25f)
        {
            PathEntity path = entity.navigator.getPath();
            if (path == null || path.NodeCountRemaining() <= 1)
            {
                Vector3 vector2 = entity.position + entity.GetForwardVector() * 0.2f;
                vector2.y += 0.4f;
                if (!Physics.Raycast(vector2 - Origin.position, Vector3.down, out var hitInfo, 3.4f, 1082195968) || hitInfo.distance > 2.2f)
                {
                    num21 = entity.jumpMaxDistance;
                    heightDiff = UnreachablePos.y - entity.position.y;
                }
            }
        }

        if (!(num21 > 0f))
        {
            return;
        }

        Vector3i vector3i = new Vector3i(Utils.Fastfloor(position.x), Utils.Fastfloor(position.y + 2.35f), Utils.Fastfloor(position.z));
        BlockValue block = entity.world.GetBlock(vector3i);
        if (!block.Block.IsMovementBlocked(entity.world, vector3i, block, BlockFace.None))
        {
            StartJump(calcYaw: true, num21, heightDiff);
            if (IsUnreachableSideJump)
            {
                UnreachablePercent += 0.1f;
                IsDestroyAreaTryUnreachable = true;
            }
        }

        IsUnreachableSideJump = false;
    }

    private void CheckWorldBlocked()
    {
        Log.Out("MoveHelperIconic - CheckWorldBlocked");
        DamageScale = 1f;
        Vector3 headPosition = entity.getHeadPosition();
        headPosition.x = entity.position.x * 0.4f + headPosition.x * 0.6f;
        headPosition.z = entity.position.z * 0.4f + headPosition.z * 0.6f;
        headPosition.y = entity.position.y;
        float num = Utils.FastClamp(ccHeight - 0.125f, 0.7f, 1.5f);
        headPosition.y += num;
        Vector3 endPos = moveToPos;
        endPos.y = headPosition.y;
        CheckBlocked(headPosition, endPos, (num >= 1f) ? 1 : 0);
        if (num >= 1f)
        {
            if (IsBlocked)
            {
                return;
            }

            Vector3 pos = headPosition;
            pos.y = entity.position.y + entity.stepHeight + 0.125f;
            endPos.y = pos.y;
            CheckBlocked(pos, endPos, 0);
            if (!IsBlocked)
            {
                return;
            }
        }

        if (!IsBlocked)
        {
            return;
        }

        WorldRayHitInfo hitInfo = HitInfo;
        endPos.y = headPosition.y + 1f;
        if (num < 1f)
        {
            headPosition.y += 0.3f;
        }

        CheckBlocked(headPosition, endPos, 2);
        if (IsBlocked)
        {
            BlockValue blockValue = hitInfo.hit.blockValue;
            float num2 = blockValue.Block.MaxDamage - blockValue.damage;
            if (HitInfo.hit.blockPos.x != Utils.Fastfloor(moveToPos.x) || HitInfo.hit.blockPos.z != Utils.Fastfloor(moveToPos.z))
            {
                HitInfo = hitInfo;
            }
            else
            {
                BlockValue blockValue2 = HitInfo.hit.blockValue;
                float num3 = blockValue2.Block.MaxDamage - blockValue2.damage;
                if (num2 * 0.7f < num3)
                {
                    HitInfo = hitInfo;
                }
            }
        }

        IsBlocked = true;
    }

    private void CheckBlocked(Vector3 pos, Vector3 endPos, int baseY)
    {
        Log.Out("MoveHelperIconic - CheckBlocked");
        IsBlocked = false;
        endPos.y -= 0.01f;
        Vector3 vector = endPos - pos;
        float num = vector.magnitude + 0.001f;
        vector *= 1f / num;
        Ray ray = new Ray(pos - vector * 0.375f, vector);
        if (num > ccRadius + 0.35f)
        {
            num = ccRadius + 0.35f;
            if (isTempMove)
            {
                num += 0.4f;
            }
        }

        if (baseY >= 2)
        {
            num += 0.21f;
        }

        if (!Voxel.Raycast(entity.world, ray, num - 0.125f + 0.375f, 1082195968, 128, 0.125f))
        {
            return;
        }

        if (baseY == 0 && Voxel.phyxRaycastHit.normal.y > 0.643f)
        {
            Vector2 vector2 = default(Vector2);
            vector2.x = Voxel.phyxRaycastHit.normal.x;
            vector2.y = Voxel.phyxRaycastHit.normal.z;
            vector2.Normalize();
            Vector2 vector3 = default(Vector2);
            vector3.x = vector.x;
            vector3.y = vector.z;
            vector3.Normalize();
            if (vector3.x * vector2.x + vector3.y * vector2.y < -0.7f)
            {
                return;
            }
        }

        if (!(Voxel.voxelRayHitInfo.hit.blockValue.Block is BlockDamage))
        {
            HitInfo = Voxel.voxelRayHitInfo.Clone();
            IsBlocked = true;
            blockedHeight = HitInfo.hit.pos.y - entity.position.y;
            Vector3 vector4 = pos - HitInfo.hit.pos;
            float sqrMagnitude = vector4.sqrMagnitude;
            if (sqrMagnitude < blockedDistSq)
            {
                blockedDistSq = sqrMagnitude;
                float num2 = 1f / Mathf.Sqrt(sqrMagnitude);
                float num3 = ccRadius + 0.4f;
                tempMoveToPos = vector4 * (num2 * num3) + HitInfo.hit.pos;
                tempMoveToPos.y = Mathf.MoveTowards(tempMoveToPos.y, moveToPos.y, 1f);
                isTempMove = true;
                obstacleCheckTickDelay = 12;
                ResetStuckCheck();
            }
        }
    }

    private void CheckBlockedUp(Vector3 pos)
    {
        IsBlocked = false;
        Vector3 headPosition = entity.getHeadPosition();
        headPosition.x = pos.x;
        headPosition.z = pos.z;
        headPosition.y -= 0.625f;
        if (Voxel.Raycast(ray: new Ray(headPosition, Vector3.up), _world: entity.world, distance: 1f, _layerMask: 1082195968, _hitMask: 128, _sphereRadius: 0.125f) && !(Voxel.voxelRayHitInfo.hit.blockValue.Block is BlockDamage))
        {
            HitInfo = Voxel.voxelRayHitInfo.Clone();
            IsBlocked = true;
            float sqrMagnitude = (pos - HitInfo.hit.pos).sqrMagnitude;
            if (sqrMagnitude < blockedDistSq)
            {
                blockedDistSq = sqrMagnitude;
                obstacleCheckTickDelay = 12;
                ResetStuckCheck();
            }
        }
    }

    private void CheckAreaBlocked()
    {
        Log.Out("MoveHelperIconic - CheckAreaBlocked");
        Vector3 headPosition = entity.getHeadPosition();
        headPosition.y = entity.position.y;
        Vector3 vector = moveToPos - headPosition;
        float f = Mathf.Atan2(vector.x, vector.z);
        float num = Mathf.Sin(f);
        float num2 = Mathf.Cos(f);
        vector.Normalize();
        Vector3 vector2 = headPosition + vector * 0.575f;
        for (float num3 = ccHeight - 0.125f; num3 > 0.225f; num3 -= 0.25f)
        {
            for (int i = 0; i < 3; i++)
            {
                float num4 = checkEdgeXs[i];
                float num5 = num4 * num2;
                float num6 = num4 * (0f - num);
                Vector3 pos = headPosition;
                pos.x += num5;
                pos.y += num3;
                pos.z += num6;
                Vector3 endPos = vector2;
                endPos.x += num5;
                endPos.y += num3;
                endPos.z += num6;
                CheckBlocked(pos, endPos, 1);
                if (IsBlocked)
                {
                    return;
                }
            }
        }
    }

    private float CalcObstacleSideStep()
    {
        Vector3 headPosition = entity.getHeadPosition();
        headPosition.y = entity.position.y;
        Vector3 vector = moveToPos - headPosition;
        if (vector.y >= 0.6f)
        {
            return 0f;
        }

        float num = Mathf.Sqrt(vector.x * vector.x + vector.z * vector.z);
        if (num <= ccRadius + 0.05f)
        {
            return 0f;
        }

        Vector2 vector2 = new Vector2(vector.x / num, vector.z / num);
        headPosition.x -= vector2.x * 0.2f;
        headPosition.z -= vector2.y * 0.2f;
        float angleRad = Mathf.Atan2(vector2.x, vector2.y);
        if (CalcObstacleSideStepArc(headPosition, angleRad, 8f, 20f, 10f) == 0f && CalcObstacleSideStepArc(headPosition, angleRad, -8f, -20f, -10f) == 0f)
        {
            return 0f;
        }

        float num2 = CalcObstacleSideStepArc(headPosition, angleRad, -48f, -20f, 11f);
        float num3 = CalcObstacleSideStepArc(headPosition, angleRad, 48f, 20f, -11f);
        if (Utils.FastAbs(num2) < num3)
        {
            if (num2 <= -48f)
            {
                return 0f;
            }

            if (num2 == 0f)
            {
                num2 = -20f;
            }

            return num2 - 50f;
        }

        if (num3 >= 48f)
        {
            return 0f;
        }

        if (num3 == 0f)
        {
            num3 = 20f;
        }

        return num3 + 50f;
    }

    private float CalcObstacleSideStepArc(Vector3 startPos, float angleRad, float dirMin, float dirMax, float dirStep)
    {
        float num = ccRadius + 0.45f;
        Vector3 vector = startPos;
        Vector3 direction = default(Vector3);
        direction.y = 0f;
        float num2 = dirMin;
        int num3 = (int)Utils.FastAbs((dirMax - dirMin) / dirStep) + 1;
        for (int i = 0; i < num3; i++)
        {
            float num4 = num2 * ((float)Math.PI / 180f);
            float f = angleRad + num4;
            direction.x = Mathf.Sin(f);
            direction.z = Mathf.Cos(f);
            float maxDistance = num / Mathf.Cos(num4);
            for (float num5 = ccHeight - 0.1f; num5 > 0.3f; num5 -= 0.9f)
            {
                vector.y = startPos.y + num5;
                if (Physics.SphereCast(vector - Origin.position, 0.1f, direction, out var _, maxDistance, 1082720256))
                {
                    return num2;
                }
            }

            num2 += dirStep;
        }

        return 0f;
    }

    private void CheckEntityBlocked(Vector3 pos, Vector3 endPos)
    {
        Log.Out("MoveHelperIconic - CheckEntityBlocked");
        Vector3 direction = endPos - pos;
        pos.y += 0.7f;
        if (!Physics.SphereCast(pos - Origin.position, 0.15f, direction, out var hitInfo, 0.8f, 524288))
        {
            return;
        }

        Transform transform = hitInfo.transform;
        if (!transform)
        {
            return;
        }

        Transform transform2 = transform.parent.Find("GameObject");
        if (!transform2)
        {
            return;
        }

        EntityAlive component = transform2.GetComponent<EntityAlive>();
        if ((bool)component && component != entity)
        {
            float sqrMagnitude = (entity.position - component.position).sqrMagnitude;
            float num = ccRadius + component.m_characterController.GetRadius() + 0.16f + 0.25f;
            if (sqrMagnitude < num * num)
            {
                BlockedEntity = component;
                blockedEntityDistSq = sqrMagnitude;
            }
        }
    }

    public new void StartJump(bool calcYaw, float distance = 0f, float heightDiff = 0f)
    {
        if (!entity.Jumping && (entity.onGround || entity.IsInElevator()) && !entity.Electrocuted)
        {
            JumpToPos = moveToPos;
            if (!calcYaw)
            {
                jumpYaw = entity.rotation.y;
            }
            else
            {
                float y = moveToPos.x - entity.position.x;
                float x = moveToPos.z - entity.position.z;
                jumpYaw = Mathf.Atan2(y, x) * 57.29578f;
            }

            entity.Jumping = true;
            entity.SetJumpDistance(distance, heightDiff);
            IsBlocked = false;
        }
    }

    private void StartSwimStroke()
    {
        if (!entity.Jumping)
        {
            JumpToPos = moveToPos;
            float y = moveToPos.x - entity.position.x;
            float x = moveToPos.z - entity.position.z;
            jumpYaw = Mathf.Atan2(y, x) * 57.29578f;
            entity.Jumping = true;
            entity.SetSwimValues(swimStrokeDelayTicks, moveToPos - entity.position);
        }
    }

    private void Push(EntityAlive blockerEntity)
    {
        Vector3 normalized = (blockerEntity.position - entity.position).normalized;
        damageResponse.Source = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Bashing, normalized);
        float massKg = EntityClass.list[entity.entityClass].MassKg;
        damageResponse.StunDuration = 0f;
        damageResponse.Strength = (int)(massKg * 0.05f);
        blockerEntity.DoRagdoll(damageResponse);
    }

    private void AttackPush(EntityAlive blockerEntity)
    {
        Vector3 normalized = (blockerEntity.position - entity.position).normalized;
        damageResponse.Source = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Bashing, normalized);
        ItemActionAttackData itemActionAttackData = entity.inventory.holdingItemData.actionData[0] as ItemActionAttackData;
        if (itemActionAttackData != null)
        {
            itemActionAttackData.hitDelegate = GetAttackHitInfo;
            if (entity.Attack(_bAttackReleased: false))
            {
                entity.Attack(_bAttackReleased: true);
            }
        }
    }

    private WorldRayHitInfo GetAttackHitInfo(out float damageMpy)
    {
        Log.Out("MoveHelperIconic - GetAttackHitInfo");
        if ((bool)BlockedEntity)
        {
            float massKg = EntityClass.list[entity.entityClass].MassKg;
            if (random.RandomFloat < 0.3f)
            {
                damageResponse.StunDuration = 0.5f;
                damageResponse.Strength = (int)(massKg * 0.4f);
            }
            else
            {
                damageResponse.StunDuration = 0f;
                damageResponse.Strength = (int)(massKg * 0.2f);
            }

            BlockedEntity.DoRagdoll(damageResponse);
        }

        damageMpy = 0f;
        return null;
    }

    private void DigStart(int forTicks)
    {
        digStartPos = entity.position;
        if (isDigging)
        {
            digForTicks = Utils.FastMax(digForTicks, forTicks);
        }
        else if (CanBreakBlocks)
        {
            digForTicks = forTicks;
            digTicks = 0f;
            digActionTicks = 18f;
            digAttacked = false;
            digForwardCount = 0f;
            AvatarController avatarController = entity.emodel.avatarController;
            avatarController.CancelEvent("EndTrigger");
            avatarController.TriggerEvent("DigStartTrigger");
            isDigging = true;
        }
    }

    private void DigUpdate()
    {
        if ((digForTicks -= 1f) <= 0f)
        {
            DigStop();
            return;
        }

        entity.SetMoveForward(0f);
        if (entity.world.IsDark())
        {
            expiryTicks = 5;
        }

        digTicks += 1f;
        if (digTicks < digActionTicks)
        {
            return;
        }

        if (!entity.emodel.avatarController.IsAnimationDigRunning())
        {
            isDigging = false;
            return;
        }

        if ((entity.position - digStartPos).sqrMagnitude >= 0.25f)
        {
            DigStop();
            return;
        }

        if (!digAttacked)
        {
            entity.emodel.avatarController.TriggerEvent("DigTrigger");
            digTicks = 0f;
            digActionTicks = 4f;
            digAttacked = true;
            return;
        }

        digActionTicks = 14f;
        digAttacked = false;
        Vector3 position = entity.position;
        position.y += 0.6f;
        Vector3 direction;
        float distance;
        if (digForwardCount > 0f)
        {
            digForwardCount -= 1f;
            direction = entity.GetForwardVector();
            distance = 1.1f;
            entity.SeekYaw(entity.rotation.y + (random.RandomFloat * 2f - 1f) * 120f, 0f, 120f);
        }
        else
        {
            position.x += (random.RandomFloat - 0.5f) * 0.3f;
            position.z += (random.RandomFloat - 0.5f) * 0.3f;
            direction = moveToPos - position;
            distance = 1.4000001f;
        }

        if (Voxel.Raycast(ray: new Ray(position, direction), _world: entity.world, distance: distance, _layerMask: 1082195968, _hitMask: 128, _sphereRadius: 0.15f))
        {
            WorldRayHitInfo voxelRayHitInfo = Voxel.voxelRayHitInfo;
            DamageMultiplier damageMultiplier = new DamageMultiplier();
            List<string> buffActions = null;
            ItemActionAttack.AttackHitInfo attackDetails = new ItemActionAttack.AttackHitInfo
            {
                hardnessScale = 1f
            };
            float num = 1f;
            ItemActionAttack itemActionAttack = entity.inventory.holdingItem.Actions[0] as ItemActionAttack;
            if (itemActionAttack != null)
            {
                num = itemActionAttack.GetDamageBlock(entity.inventory.holdingItemData.actionData[0].invData.itemValue, BlockValue.Air);
            }

            ItemActionAttack.Hit(voxelRayHitInfo, entity.entityId, EnumDamageTypes.Bashing, num, num, 1f, 1f, 0f, 0.05f, "organic", damageMultiplier, buffActions, attackDetails);
        }
        else if (digForwardCount == 0f)
        {
            digForwardCount = 2f;
        }
        else
        {
            digForwardCount = 0f;
        }
    }

    private void DigStop()
    {
        if (isDigging)
        {
            isDigging = false;
            entity.emodel.avatarController.TriggerEvent("EndTrigger");
        }
    }

    public new  float CalcBlockedDistanceSq()
    {        
        if (HitInfo.hit.pos != null)
        {
            if (entity != null)
            {
                Vector3 pos = HitInfo.hit.pos;
                Vector3 position = entity.position;
                float num = pos.x - position.x;
                float num2 = pos.z - position.z;
                return num * num + num2 * num2;
            }
            Log.Out("MoveHelperIconic - Entity is Null");
        }

        Log.Out("MoveHelperIconic - No Hit Info Available");
        return 0f;
    }

    public new void ClearBlocked()
    {
        IsBlocked = false;
        BlockedTime = 0f;
    }

    public new void ClearTempMove()
    {
        isTempMove = false;
    }

    private void ResetStuckCheck()
    {
        SideStepAngle = 0f;
        moveToTicks = 0;
        moveToFailCnt = 0;
        if (isTempMove)
        {
            moveToDistance = CalcTempMoveDist();
        }
        else
        {
            moveToDistance = CalcMoveDist();
        }
    }

    private float CalcMoveDist()
    {
        Vector3 position = entity.position;
        float num = moveToPos.x - position.x;
        float num2 = moveToPos.z - position.z;
        float num3 = moveToPos.y - position.y;
        return Mathf.Sqrt(num * num + num3 * num3 + num2 * num2);
    }

    private float CalcTempMoveDist()
    {
        Vector3 position = entity.position;
        float num = tempMoveToPos.x - position.x;
        float num2 = tempMoveToPos.z - position.z;
        float num3 = tempMoveToPos.y - position.y;
        return Mathf.Sqrt(num * num + num3 * num3 + num2 * num2);
    }

    private Vector3 CalcBlockCenterXZ(Vector3 pos)
    {
        pos.x = (float)Utils.Fastfloor(pos.x) + 0.5f;
        pos.z = (float)Utils.Fastfloor(pos.z) + 0.5f;
        return pos;
    }

    private Vector3 CalcBlockCenter(Vector3 pos)
    {
        pos.x = (float)Utils.Fastfloor(pos.x) + 0.5f;
        pos.y = (float)Utils.Fastfloor(pos.y) + 0.5f;
        pos.z = (float)Utils.Fastfloor(pos.z) + 0.5f;
        return pos;
    }

    public new void CalcIfUnreachablePos()
    {
        Log.Out("MoveHelperIconic - CalcIfUnreachablePos");
        IsUnreachableSideJump = false;
        if (entity.Jumping)
        {
            return;
        }

        IsUnreachableAbove = false;
        IsUnreachableSide = false;
        PathEntity path = entity.navigator.getPath();
        if (path == null)
        {
            return;
        }

        Vector3 toPos = path.toPos;
        Vector3 rawEndPos = path.rawEndPos;
        float num = rawEndPos.x - toPos.x;
        float num2 = rawEndPos.z - toPos.z;
        float num3 = num * num + num2 * num2;
        float num4 = toPos.y - rawEndPos.y;
        if (num4 > 2.2f && num3 < 25f)
        {
            IsUnreachableAbove = true;
            UnreachablePos = rawEndPos;
        }

        if (!(num4 >= -1.5f) || !(num3 >= 1.44f))
        {
            return;
        }

        IsUnreachableSide = true;
        UnreachablePos = rawEndPos;
        float jumpMaxDistance = entity.jumpMaxDistance;
        if (jumpMaxDistance > 0f && num4 < 0.5f + jumpMaxDistance * 0.5f)
        {
            jumpMaxDistance += 3.4f;
            if (num3 <= jumpMaxDistance * jumpMaxDistance)
            {
                IsUnreachableSideJump = true;
            }
        }
    }

    public new bool IsMoveToAbove()
    {
        if (moveToPos.y - entity.position.y > 1.9f)
        {
            return true;
        }

        return false;
    }

    public new bool FindDestroyPos(ref Vector3 destroyPos, bool isLookFar)
    {
        Log.Out("MoveHelperIconic - FindDestroyPos");
        int num = int.MaxValue;
        Vector3i vector3i = Vector3i.zero;
        ChunkCluster chunkCache = entity.world.ChunkCache;
        Vector3i vector3i2 = World.worldToBlockPos(destroyPos);
        int i = 1;
        int num2 = 1;
        if (isLookFar)
        {
            i = random.RandomRange(5, 11);
            num2 = -1;
            vector3i2.y -= 2;
        }

        bool flag = false;
        int num3 = random.RandomRange(0, 4);
        Vector3i vector3i3 = default(Vector3i);
        for (; i >= 1 && i <= 11; i += num2)
        {
            int num4 = i * 2;
            for (int j = -2; j <= 2; j++)
            {
                vector3i3.y = vector3i2.y + j;
                for (int k = 0; k < 4; k++)
                {
                    DestroyData destroyData = MoveHelperIconic.destroyData[k + num3];
                    int num5 = destroyData.offsetX * i;
                    int num6 = destroyData.offsetZ * i;
                    vector3i3.x = vector3i2.x + num5;
                    vector3i3.z = vector3i2.z + num6;
                    for (int l = 0; l < num4; l++)
                    {
                        BlockValue block = chunkCache.GetBlock(vector3i3);
                        if (!block.isair)
                        {
                            Block block2 = block.Block;
                            Log.Out("MoveHelperIconic - Block2");
                            if (block2.IsMovementBlocked(entity.world, vector3i3, block, BlockFace.None) && block2.StabilitySupport)
                            {
                                Vector3i vector3i4 = vector3i3;
                                vector3i4.y++;
                                BlockValue block3 = chunkCache.GetBlock(vector3i4);
                                if (!block3.isair)
                                {
                                    Block block4 = block3.Block;
                                    Log.Out("MoveHelperIconic - Block4");
                                    if (block4.IsMovementBlocked(entity.world, vector3i4, block3, BlockFace.None) && block4.StabilitySupport)
                                    {
                                        bool flag2 = false;
                                        int num7 = block2.MaxDamagePlusDowngrades - block.damage;
                                        if (block2.shape.IsTerrain())
                                        {
                                            num7 *= 50;
                                        }
                                        else
                                        {
                                            flag2 = true;
                                        }

                                        int num8 = block4.MaxDamagePlusDowngrades - block3.damage;
                                        if (block4.shape.IsTerrain())
                                        {
                                            num8 *= 50;
                                        }
                                        else
                                        {
                                            flag2 = true;
                                        }

                                        num7 += num8;
                                        if (num7 < num && (!flag || flag2) && IsABlockSideOpen(vector3i3))
                                        {
                                            flag = flag2;
                                            num = num7;
                                            vector3i = vector3i3;
                                        }
                                    }
                                }
                            }
                        }

                        vector3i3.x += destroyData.stepX;
                        vector3i3.z += destroyData.stepZ;
                    }
                }
            }

            if (flag)
            {
                break;
            }
        }

        if (num > 999999)
        {
            return false;
        }

        destroyPos = vector3i.ToVector3CenterXZ();
        destroyPos.y += 1f;
        return true;
    }

    private bool IsABlockSideOpen(Vector3i checkPos)
    {
        ChunkCluster chunkCache = entity.world.ChunkCache;
        for (int i = 0; i < blockOpenOffsets.Length; i += 2)
        {
            Vector3i vector3i = checkPos;
            vector3i.x += blockOpenOffsets[i];
            vector3i.z += blockOpenOffsets[i + 1];
            BlockValue block = chunkCache.GetBlock(vector3i);
            if (!block.Block.IsMovementBlocked(entity.world, vector3i, block, BlockFace.None))
            {
                return true;
            }
        }

        return false;
    }

}

