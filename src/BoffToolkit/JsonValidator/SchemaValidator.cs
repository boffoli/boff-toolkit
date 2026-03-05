using System;
using AutoFixture;
using AutoFixture.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace BoffToolkit.JsonValidator {
    /// <summary>
    /// Provides functionality for validating JSON content against a specified JSON schema.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="SchemaValidator"/> class with a specified JSON schema.
    /// </remarks>
    public class SchemaValidator(string jsonSchema) {
        private readonly string _jsonSchema = jsonSchema ?? throw new ArgumentNullException(nameof(jsonSchema), "A JSON schema is required.");
        private static readonly Fixture _fixture = new();

        /// <summary>
        /// Validates a JSON string against the JSON schema provided during initialization.
        /// </summary>
        /// <param name="jsonContent">The JSON string to validate.</param>
        public void Validate(string jsonContent) => ValidationHelper.ValidateContent(jsonContent, _jsonSchema);

        /// <summary>
        /// Validates an object by serializing it into JSON and checking it against the provided schema.
        /// </summary>
        /// <param name="instance">The object instance to validate.</param>
        public void Validate(object instance) => ValidationHelper.ValidateContent(JsonConvert.SerializeObject(instance), _jsonSchema);

        /// <summary>
        /// Validates a type by creating an instance, serializing it into JSON, and checking it against the schema.
        /// </summary>
        /// <param name="type">The type of the object to validate.</param>
        public void Validate(Type type) => ValidationHelper.ValidateContent(SerializeInstance(type), _jsonSchema);

        /// <summary>
        /// Attempts to validate a JSON string against the schema and returns a validation result.
        /// </summary>
        /// <param name="jsonContent">The JSON string to validate.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating success or failure of validation.</returns>
        public ValidationResult TryValidate(string jsonContent) => ValidationHelper.TryValidateContent(jsonContent, _jsonSchema);

        /// <summary>
        /// Attempts to validate an object instance against the schema by serializing it into JSON.
        /// </summary>
        /// <param name="instance">The object to validate.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating success or failure of validation.</returns>
        public ValidationResult TryValidate(object instance) => ValidationHelper.TryValidateContent(JsonConvert.SerializeObject(instance), _jsonSchema);

        /// <summary>
        /// Attempts to validate a type by creating an instance, serializing it into JSON, and validating it against the schema.
        /// </summary>
        /// <param name="type">The type to validate.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating success or failure of validation.</returns>
        public ValidationResult TryValidate(Type type) => ValidationHelper.TryValidateContent(SerializeInstance(type), _jsonSchema);

        /// <summary>
        /// Validates a JSON string against a specified schema.
        /// </summary>
        /// <param name="jsonContent">The JSON string to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        public static void Validate(string jsonContent, string jsonSchema) => ValidationHelper.ValidateContent(jsonContent, jsonSchema);

        /// <summary>
        /// Validates an object instance against a specified schema by serializing it into JSON.
        /// </summary>
        /// <param name="instance">The object to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        public static void Validate(object instance, string jsonSchema) => ValidationHelper.ValidateContent(JsonConvert.SerializeObject(instance), jsonSchema);

        /// <summary>
        /// Validates a type by creating an instance, serializing it into JSON, and validating it against a schema.
        /// </summary>
        /// <param name="type">The type to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        public static void Validate(Type type, string jsonSchema) => ValidationHelper.ValidateContent(SerializeInstance(type), jsonSchema);

        /// <summary>
        /// Attempts to validate a JSON string against a specified schema.
        /// </summary>
        /// <param name="jsonContent">The JSON string to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating whether the validation succeeded or failed.</returns>
        public static ValidationResult TryValidate(string jsonContent, string jsonSchema) => ValidationHelper.TryValidateContent(jsonContent, jsonSchema);

        /// <summary>
        /// Attempts to validate an object instance against a schema by serializing it into JSON.
        /// </summary>
        /// <param name="instance">The object to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating whether the validation succeeded or failed.</returns>
        public static ValidationResult TryValidate(object instance, string jsonSchema) => ValidationHelper.TryValidateContent(JsonConvert.SerializeObject(instance), jsonSchema);

        /// <summary>
        /// Attempts to validate a type by creating an instance, serializing it into JSON, and validating it against a schema.
        /// </summary>
        /// <param name="type">The type to validate.</param>
        /// <param name="jsonSchema">The schema to validate against.</param>
        /// <returns>A <see cref="ValidationResult"/> indicating whether the validation succeeded or failed.</returns>
        public static ValidationResult TryValidate(Type type, string jsonSchema) => ValidationHelper.TryValidateContent(SerializeInstance(type), jsonSchema);

        /// <summary>
        /// Serializes an instance of the specified type into a JSON string.
        /// </summary>
        /// <param name="type">The type to create an instance of and serialize.</param>
        /// <returns>A JSON string representing the created instance.</returns>
        private static string SerializeInstance(Type type) {
            var context = new SpecimenContext(_fixture as ISpecimenBuilder);
            var instance = context.Resolve(type);
            return JsonConvert.SerializeObject(instance);
        }

        // Classe helper interna per la logica di validazione
        private static class ValidationHelper {
            public static void ValidateContent(string jsonContent, string jsonSchema) {
                var result = TryValidateContent(jsonContent, jsonSchema);
                if (!result.IsValid) {
                    throw new JsonException($"Validazione fallita. Errori: {string.Join(", ", result.ErrorMessages)}");
                }
            }

            public static ValidationResult TryValidateContent(string jsonContent, string jsonSchema) {
                try {
                    var verbose = string.Equals(
                        Environment.GetEnvironmentVariable("BOFF_JSON_VALIDATOR_VERBOSE"),
                        "true",
                        StringComparison.OrdinalIgnoreCase);
                    if (verbose) {
                        Console.WriteLine("Schema JSON:");
                        Console.WriteLine(jsonSchema); // Stampa lo schema JSON
                    }
                    var schema = JSchema.Parse(jsonSchema);

                    if (verbose) {
                        Console.WriteLine("Contenuto JSON:");
                        Console.WriteLine(jsonContent); // Stampa il contenuto JSON
                    }
                    var token = JToken.Parse(jsonContent);
                    var isValid = token.IsValid(schema, out IList<ValidationError> validationErrors);

                    var errorMessages = validationErrors.Select(e => e.Message).ToList();
                    return new ValidationResult(isValid, errorMessages);
                }
                catch (JsonReaderException ex) {
                    return new ValidationResult(false, new List<string> { $"Errore di parsing JSON: {ex.Message}" });
                }
                catch (JSchemaException ex) {
                    return new ValidationResult(false, new List<string> { $"Errore nello schema JSON: {ex.Message}" });
                }
                catch (Exception ex) {
                    return new ValidationResult(false, new List<string> { $"Errore sconosciuto durante la validazione: {ex.Message}" });
                }
            }
        }
    }

    /// <summary>
    /// Represents the result of a validation, including the validity status and any error messages.
    /// </summary>
    public class ValidationResult(bool isValid, IList<string> errorMessages) {
        /// <summary>
        /// Gets a value indicating whether the validation was successful.
        /// </summary>
        /// <value>
        /// <c>true</c> if the validation passed; otherwise, <c>false</c>.
        /// </value>
        public bool IsValid { get; } = isValid;

        /// <summary>
        /// Gets a list of error messages describing validation failures, if any.
        /// </summary>
        /// <value>
        /// A collection of error messages returned during validation.
        /// </value>
        /// <exception cref="ArgumentNullException">
        /// </exception>
        public IList<string> ErrorMessages { get; } = errorMessages ?? throw new ArgumentNullException(nameof(errorMessages), "Error messages are required.");
    }
}
