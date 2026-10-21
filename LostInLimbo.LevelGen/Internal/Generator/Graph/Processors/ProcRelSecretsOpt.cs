using System;

namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// Parameters of <see cref="ProcRelSecrets"/>: how many secret nodes the pass appends and
/// how likely each additional secret is to hang off an existing secret instead of an
/// ordinary node.
/// </summary>
/// <remarks>
/// Zero-valued behavior: <see cref="Amount"/> 0 disables the pass entirely;
/// <see cref="ChanceSecretInSecret"/> 0 never nests a secret behind another secret.
/// </remarks>
internal sealed record ProcRelSecretsOpt
{
  /// <summary>
  /// Default instance used by builders that do not pass custom options: one secret node
  /// and a ten percent chance of secret-in-secret nesting.
  /// </summary>
  public static ProcRelSecretsOpt Default { get; } = new()
  {
    Amount = 1,
    ChanceSecretInSecret = 0.1f,
  };

  /// <summary>
  /// Amount of secret nodes to append; 0 disables the pass. Must not be negative.
  /// </summary>
  public int Amount { get; init; } = 1;

  /// <summary>
  /// Per-secret chance that, instead of attaching to a random ordinary node, the new
  /// secret attaches to a secret created earlier by this pass - a secret-in-secret.
  /// The first secret always attaches to an ordinary node. Must be a finite value within
  /// [0, 1].
  /// </summary>
  public float ChanceSecretInSecret { get; init; } = 0.1f;

  /// <summary>
  /// Checks that these options are valid.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">
  /// Some parameter has invalid value.
  /// </exception>
  public void Validate()
  {
    if (Amount < 0)
      throw new ArgumentOutOfRangeException(nameof(Amount), Amount, "Amount must not be negative.");
    if (!float.IsFinite(ChanceSecretInSecret) || ChanceSecretInSecret < 0f || ChanceSecretInSecret > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceSecretInSecret), ChanceSecretInSecret, "ChanceSecretInSecret must be a finite value within [0, 1].");
  }
}
