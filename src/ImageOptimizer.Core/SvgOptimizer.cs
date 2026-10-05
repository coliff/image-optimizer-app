using System.Text;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace ImageOptimizer.Core;

/// <summary>
/// SVG: SVGO's own browser build (svgo.browser.js, fetched with the other tools) running in the embedded
/// Jint JavaScript engine, so no Node.js install is needed.
/// </summary>
public sealed class SvgOptimizer(Func<string?> locateScript) : IFormatOptimizer
{
  public const string ScriptName = "svgo.browser.js";

  private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

  // Jint engines aren't thread-safe and loading SVGO takes a moment, so share one engine and take turns.
  private readonly SemaphoreSlim _gate = new(1, 1);
  private Engine? _engine;
  private JsValue? _optimize;

  public ImageFormat Format => ImageFormat.Svg;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    // The result is always written as UTF-8, so text in any other encoding would come out garbled.
    var bytes = await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false);
    string input;
    try
    {
      input = StrictUtf8.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(StrictUtf8.Preamble) ? StrictUtf8.Preamble.Length : 0));
    }
    catch (DecoderFallbackException)
    {
      return CandidateSet.Skip(Strings.SvgNotUtf8);
    }
    var config = BuildConfig(context.Settings);

    string output;
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      output = await Task.Run(() => Optimize(input, config), cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _gate.Release();
    }

    if (!output.Contains("<svg", StringComparison.Ordinal))
      throw new ToolFailedException("svgo", 1, "SVGO returned something that isn't an SVG");

    var candidate = context.WorkFile("svgo.svg");
    await File.WriteAllTextAsync(candidate, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    return new CandidateSet([new Candidate(candidate)]);
  }

  internal static string BuildConfig(OptimizerSettings settings)
  {
    var overrides = new Dictionary<string, bool>();
    if (!settings.StripMetadata)
    {
      overrides["removeComments"] = false;
      overrides["removeMetadata"] = false;
      overrides["removeEditorsNSData"] = false;
    }

    return JsonSerializer.Serialize(new
    {
      multipass = settings.MaximumCompression,
      plugins = new object[] { new { name = "preset-default", @params = new { overrides } } },
    });
  }

  private string Optimize(string input, string config)
  {
    try
    {
      if (_engine is null || _optimize is null)
      {
        var script = locateScript() ?? throw new ToolNotFoundException("svgo");
        var engine = new Engine();
        engine.Modules.Add("svgo", File.ReadAllText(script));
        _optimize = engine.Modules.Import("svgo").Get("optimize");
        _engine = engine;
      }

      var options = _engine.Evaluate($"({config})");
      return _engine.Invoke(_optimize, input, options).AsObject().Get("data").AsString();
    }
    catch (JintException ex)
    {
      throw new ToolFailedException("svgo", 1, ex.Message);
    }
  }
}
