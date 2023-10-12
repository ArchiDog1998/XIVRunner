﻿using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.GeneratedSheets;
using System.Numerics;

namespace XIVRunner;

/// <summary>
/// Make your character can be automatically moved for FFXIV in Dalamud.
/// </summary>
public class XIVRunner : IDisposable
{
    private readonly OverrideMovement _movementManager;
    private readonly OverrideAFK _overrideAFK;

    /// <summary>
    /// The Navigate points.
    /// </summary>
    public Queue<Vector3> NaviPts { get; } = new Queue<Vector3>(64);

    /// <summary>
    /// Auto run along the pts.
    /// </summary>
    public bool RunAlongPts { get; set; }

    /// <summary>
    /// If the player is close enough to the point, It'll remove the pt.
    /// </summary>
    public float Precision 
    {
        get => _movementManager.Precision;
        set => _movementManager.Precision = value;
    }

    /// <summary>
    /// The mount id.
    /// </summary>
    public uint? MountId { get; set; }

    internal bool IsFlying => Service.Condition[ConditionFlag.InFlight] || Service.Condition[ConditionFlag.Diving];
    internal bool IsMounted => Service.Condition[ConditionFlag.Mounted];

    /// <summary>
    /// The way to create this.
    /// </summary>
    /// <param name="pluginInterface"></param>
    /// <returns></returns>
    public static XIVRunner Create(DalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();
        return new XIVRunner();
    }

    private XIVRunner()
    {
        _movementManager = new OverrideMovement();
        _overrideAFK = new OverrideAFK();
        Service.Framework.Update += Update;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _movementManager.Dispose();
        Service.Framework.Update -= Update;
    }

    private  void Update(IFramework framework)
    {
        if (Service.ClientState.LocalPlayer == null) return;
        if (Service.Condition == null || !Service.Condition.Any()) return;

        UpdateDirection();
    }

    private void UpdateDirection()
    {
        var positon = Service.ClientState.LocalPlayer?.Position ?? default;

        if (!RunAlongPts)
        {
            _movementManager.DesiredPosition = null;
            return;
        }

    GetPT:
        if (NaviPts.Any())
        {
            var target = NaviPts.Peek();

            var dir = target - positon;
            if (IsFlying ? dir.Length() < Precision : new Vector2(dir.X, dir.Z).Length() < Precision)
            {
                NaviPts.Dequeue();
                goto GetPT;
            }

            WhenFindTheDesirePosition(target);
        }
        else
        {
            WhenNotFindTheDesirePosition();
        }
    }

    private void WhenFindTheDesirePosition(Vector3 target)
    {
        _overrideAFK.ResetTimers();
        if (_movementManager.DesiredPosition != target)
        {
            _movementManager.DesiredPosition = target;
            TryMount();
        }
        else
        {
            TryFly();
            TryRunFast();
        }
    }

    private void WhenNotFindTheDesirePosition()
    {
        if (_movementManager.DesiredPosition != null)
        {
            _movementManager.DesiredPosition = null;
            if (IsMounted && !IsFlying)
            {
                ExecuteDismount();
            }
        }
    }

    private static readonly Dictionary<ushort, bool> canFly = new Dictionary<ushort, bool>();
    private void TryFly()
    {
        if (Service.Condition[ConditionFlag.Jumping]) return;
        if (IsFlying) return;
        if (!IsMounted) return;

        bool hasFly = canFly.TryGetValue(Service.ClientState.TerritoryType, out var fly);

        //TODO: Whether it is possible to fly from the current territory.
        if (fly || !hasFly)
        {
            ExecuteJump();

            if (!hasFly)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    canFly[Service.ClientState.TerritoryType] = IsFlying;
                });
            }
        }
    }

    private void TryRunFast()
    {
        if (IsMounted) return;

        //TODO: add jobs actions for moving fast.

        //ExecuteActionSafe(ActionType.Action, 3); // Sprint.
    }

    private void TryMount()
    {
        if (IsMounted) return;

        var territory = Service.Data.GetExcelSheet<TerritoryType>()?.GetRow(Service.ClientState.TerritoryType);
        if (territory?.Mount ?? false)
        {
            ExecuteMount();
        }
    }

    private static unsafe bool ExecuteActionSafe(ActionType type, uint id)
        => ActionManager.Instance()->GetActionStatus(type, id) == 0 
        && ActionManager.Instance()->UseAction(type, id);

    private bool ExecuteMount()
    {
        if (MountId.HasValue && ExecuteActionSafe(ActionType.Mount, MountId.Value))
        {
            return true;
        }
        else
        {
            return ExecuteActionSafe(ActionType.GeneralAction, 9);
        }
    }
    private bool ExecuteDismount() => ExecuteActionSafe(ActionType.GeneralAction, 23);
    private bool ExecuteJump() => ExecuteActionSafe(ActionType.GeneralAction, 2);
}
