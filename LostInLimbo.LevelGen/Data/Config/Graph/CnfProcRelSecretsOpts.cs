using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// <para>Configuration of the secret-nodes processor (<c>ProcRelSecrets</c>).</para>
/// </summary>
/// <remarks>
/// Zero-valued behavior: <see cref="Amount"/> 0 disables the processor entirely;
/// <see cref="ChanceSecretInSecret"/> 0 never nests a secret behind another secret.
/// </remarks>
public sealed record CnfProcRelSecretsOpts
{
  /// <summary>
  /// Default instance: one secret node and a ten percent chance of secret-in-secret
  /// nesting.
  /// </summary>
  public static CnfProcRelSecretsOpts Default { get; } = new()
  {
    Amount = 1,
    ChanceSecretInSecret = 0.1f,
  };

  /// <summary>
  /// Amount of secret nodes to append; 0 disables the processor. Must not be negative.
  /// </summary>
  public int Amount { get; init; } = 1;

  /// <summary>
  /// Per-secret chance that, instead of attaching to a random ordinary node, the new
  /// secret attaches to a secret created earlier by the same processor - a
  /// secret-in-secret. The first secret always attaches to an ordinary node. Must be a
  /// finite value within [0, 1].
  /// </summary>
  public float ChanceSecretInSecret { get; init; } = 0.1f;

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
  public void Validate()
  {
    if (Amount < 0)
      throw new ArgumentOutOfRangeException(nameof(Amount), Amount, "Amount must not be negative.");
    if (!float.IsFinite(ChanceSecretInSecret) || ChanceSecretInSecret < 0f || ChanceSecretInSecret > 1f)
      throw new ArgumentOutOfRangeException(nameof(ChanceSecretInSecret), ChanceSecretInSecret, "ChanceSecretInSecret must be a finite value within [0, 1].");
  }
}
