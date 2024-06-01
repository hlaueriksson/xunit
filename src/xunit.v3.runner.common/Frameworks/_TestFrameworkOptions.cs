using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Xunit.Runner.Common;

/// <summary>
/// Represents options passed to a test framework for discovery or execution.
/// </summary>
[DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
public class _TestFrameworkOptions : _ITestFrameworkDiscoveryOptions, _ITestFrameworkExecutionOptions
{
	readonly Dictionary<string, string> properties = new();

	// Force users to use one of the factory methods
	_TestFrameworkOptions(string? optionsJson = null)
	{
		if (optionsJson is not null)
		{
			if (!JsonDeserializer.TryDeserialize(optionsJson, out var json))
				throw new ArgumentException("Invalid JSON", nameof(optionsJson));
			if (json is not IReadOnlyDictionary<string, object> root)
				throw new ArgumentException("JSON options must be a top-level object", nameof(optionsJson));

			foreach (var kvp in root)
				properties[kvp.Key] = kvp.Value.ToString() ?? throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Option value '{0}' for key '{1}' is null", kvp.Value, kvp.Key), nameof(optionsJson));
		}
	}

	/// <summary>
	/// INTERNAL METHOD, FOR TESTING PURPOSES ONLY. DO NOT CALL.
	/// </summary>
	public static _TestFrameworkOptions Empty() =>
		new();

	/// <summary>
	/// Creates an instance of <see cref="_TestFrameworkOptions"/> for discovery purposes.
	/// </summary>
	/// <param name="configuration">The configuration to copy values from.</param>
	public static _ITestFrameworkDiscoveryOptions ForDiscovery(TestAssemblyConfiguration configuration)
	{
		Guard.ArgumentNotNull(configuration);

		_ITestFrameworkDiscoveryOptions result = new _TestFrameworkOptions();

		result.SetCulture(configuration.Culture);
		result.SetDiagnosticMessages(configuration.DiagnosticMessages);
		result.SetIncludeSourceInformation(configuration.IncludeSourceInformation);
		result.SetInternalDiagnosticMessages(configuration.InternalDiagnosticMessages);
		result.SetMethodDisplay(configuration.MethodDisplay);
		result.SetMethodDisplayOptions(configuration.MethodDisplayOptions);
		result.SetPreEnumerateTheories(configuration.PreEnumerateTheories);
		result.SetSynchronousMessageReporting(configuration.SynchronousMessageReporting);

		return result;
	}

	/// <summary>
	/// Creates an instance of <see cref="_TestFrameworkOptions"/> for discovery purposes.
	/// </summary>
	/// <param name="optionsJson">The serialized discovery options.</param>
	public static _ITestFrameworkDiscoveryOptions ForDiscoveryFromSerialization(string optionsJson) =>
		new _TestFrameworkOptions(optionsJson);

	/// <summary>
	/// Creates an instance of <see cref="_TestFrameworkOptions"/> for execution purposes.
	/// </summary>
	/// <param name="configuration">The configuration to copy values from.</param>
	public static _ITestFrameworkExecutionOptions ForExecution(TestAssemblyConfiguration configuration)
	{
		Guard.ArgumentNotNull(configuration);

		_ITestFrameworkExecutionOptions result = new _TestFrameworkOptions();

		result.SetCulture(configuration.Culture);
		result.SetDiagnosticMessages(configuration.DiagnosticMessages);
		result.SetDisableParallelization(!configuration.ParallelizeTestCollections);
		result.SetExplicitOption(configuration.ExplicitOption);
		result.SetFailSkips(configuration.FailSkips);
		result.SetFailTestsWithWarnings(configuration.FailTestsWithWarnings);
		result.SetInternalDiagnosticMessages(configuration.InternalDiagnosticMessages);
		result.SetMaxParallelThreads(configuration.MaxParallelThreads);
		result.SetParallelAlgorithm(configuration.ParallelAlgorithm);
		result.SetSeed(configuration.Seed);
		result.SetStopOnTestFail(configuration.StopOnFail);
		result.SetSynchronousMessageReporting(configuration.SynchronousMessageReporting);

		return result;
	}

	/// <summary>
	/// Creates an instance of <see cref="_TestFrameworkOptions"/> for execution purposes.
	/// </summary>
	/// <param name="optionsJson">The serialized execution options.</param>
	public static _ITestFrameworkExecutionOptions ForExecutionFromSerialization(string optionsJson) =>
		new _TestFrameworkOptions(optionsJson);

	/// <summary>
	/// Gets a value from the options collection.
	/// </summary>
	/// <typeparam name="TValue">The type of the value.</typeparam>
	/// <param name="name">The name of the value.</param>
	/// <returns>Returns the value.</returns>
	public TValue? GetValue<TValue>(string name)
	{
		Guard.ArgumentNotNullOrEmpty(name);

		if (properties.TryGetValue(name, out var result))
		{
			if (result is null)
				return default;

			if (typeof(TValue) == typeof(string))
				return (TValue)(object)result;

			var targetType = typeof(TValue).UnwrapNullable();
			return (TValue)Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
		}

		return default;
	}

	/// <summary>
	/// Sets a value into the options collection.
	/// </summary>
	/// <typeparam name="TValue">The type of the value.</typeparam>
	/// <param name="name">The name of the value.</param>
	/// <param name="value">The value.</param>
	public void SetValue<TValue>(
		string name,
		TValue value)
	{
		if (value is null)
			properties.Remove(name);
		else
		{
			if (typeof(TValue) == typeof(string))
				properties[name] = (string)(object)value;
			else
				properties[name] = (string)Convert.ChangeType(value, typeof(string), CultureInfo.InvariantCulture);
		}
	}

	string ToDebuggerDisplay()
		=> string.Format(CultureInfo.CurrentCulture, "{{ {0} }}", string.Join(", ", properties.Select(p => string.Format(CultureInfo.CurrentCulture, "{{ {0} = {1} }}", p.Key, p.Value)).ToArray()));

	/// <inheritdoc/>
	public string ToJson()
	{
		var buffer = new StringBuilder();

		using (var serializer = new JsonObjectSerializer(buffer))
			foreach (var kvp in properties)
				serializer.Serialize(kvp.Key, kvp.Value);

		return buffer.ToString();
	}
}
