using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Proc;

/// <summary>
/// <para>Configuration of the secret-nodes processor (<c>ProcRelSecrets</c>).</para>
/// </summary>
/// <remarks>
/// Zero-valued behavior: <see cref="Amount"/> 0 disables the processor entirely;
/// <see cref="ChanceSecretInSecret"/> 0 never nests a secret behind another secret;
/// <see cref="ChanceTwoPassages"/> 0 never creates a secret with two passages, so the
/// three two-passage parameters never influence generation.
/// </remarks>
public sealed record CnfProcRelSecretsOpt
{
  /// <summary>Maximum allowed amount - keeps generation cost bounded.</summary>
  public const int MaxAmount = 100;

  //

  /// <summary>
  /// Default instance: one secret node and a ten percent chance of secret-in-secret
  /// nesting.
  /// </summary>
  public static CnfProcRelSecretsOpt Default { get; } = new()
  {
    Amount = 1,
    ChanceSecretInSecret = 0.1f,
  };

  /// <summary>
  /// Number of secret nodes to append; 0 disables the processor. Must not be negative. The hard cap is <see cref="MaxAmount"/>.
  /// </summary>
  public int Amount { get; init; } = 1;

  /// <summary>
  /// Per-secret chance that, instead of attaching to a random ordinary node, the new
  /// secret attaches to a secret created earlier by the same processor - a
  /// secret-in-secret. The first secret always attaches to an ordinary node.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceSecretInSecret { get; init; } = 0.1f;

  /// <summary>
  /// Per-secret chance that secret node has two passages instead of one.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceTwoPassages { get; init; } = 0.0f;

  /// <summary>
  /// When a secret gets two passages, per-secret chance that the passage in and the
  /// passage out connect the same node (<c>A -&gt; S -&gt; A</c>, both one way) instead of
  /// leading to a different nearby node. Only consulted when the host node still has two
  /// free relationship slots; otherwise the processor searches for a different exit node.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceReturnToHost { get; init; } = 0.5f;

  /// <summary>
  /// When a secret gets two passages, per-secret chance that conditional passages are
  /// ignored while searching for the exit node - the secret may then shortcut past
  /// conditional passages. When the roll misses, exit nodes that are only reachable over
  /// conditional passages are not eligible. Rolled once at the start of each two-passage
  /// secret, so a secret either allows conditional passages on its exit path or not at
  /// all. Only consulted when <see cref="ChanceTwoPassages"/> hits.
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float ChanceIgnoreConditionals { get; init; } = 0.1f;

  /// <summary>
  /// Maximum relationship distance (hops, ignoring passage direction) between the host
  /// node and the exit node of a two-passage secret. Nearer exit nodes are proportionally
  /// more likely to be picked; nodes at the same distance are equally likely. Only
  /// consulted when <see cref="ChanceTwoPassages"/> hits.
  /// <para>Must be at least 1.</para>
  /// </summary>
  public int MaxExitDistance { get; init; } = 3;

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (Amount < 0)
      throw new ArgumentOutOfRangeException(nameof(Amount), Amount, "Amount must not be negative.");
    if (Amount > MaxAmount)
      throw new ArgumentOutOfRangeException(nameof(Amount), Amount, $"Amount must not exceed {MaxAmount}.");
    if (!float.IsFinite(ChanceSecretInSecret) || ChanceSecretInSecret < 0f || ChanceSecretInSecret > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceSecretInSecret), ChanceSecretInSecret, "ChanceSecretInSecret must be a finite value within [0, 1].");
    if (!float.IsFinite(ChanceTwoPassages) || ChanceTwoPassages < 0f || ChanceTwoPassages > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceTwoPassages), ChanceTwoPassages, "ChanceTwoPassages must be a finite value within [0, 1].");
    if (!float.IsFinite(ChanceReturnToHost) || ChanceReturnToHost < 0f || ChanceReturnToHost > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceReturnToHost), ChanceReturnToHost, "ChanceReturnToHost must be a finite value within [0, 1].");
    if (!float.IsFinite(ChanceIgnoreConditionals) || ChanceIgnoreConditionals < 0f || ChanceIgnoreConditionals > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceIgnoreConditionals), ChanceIgnoreConditionals, "ChanceIgnoreConditionals must be a finite value within [0, 1].");
    if (MaxExitDistance < 1)
      throw new ArgumentOutOfRangeException(nameof(MaxExitDistance), MaxExitDistance, "MaxExitDistance must be at least 1.");
  }
}
