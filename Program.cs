// ---------------------------------------------------------------------------
// Copyright (c) Coalition of the good hearted engineers. All rights reserved.
// ---------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Litmus
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string classFilePath;
            string razorFilePath;

            string currentDir = Directory.GetCurrentDirectory();

            if (args.Length == 0)
            {
                var csFiles = Directory.GetFiles(currentDir, "*.cs");
                var razorFiles = Directory.GetFiles(currentDir, "*.razor");

                if (csFiles.Length == 1 && razorFiles.Length == 1)
                {
                    classFilePath = csFiles[0];
                    razorFilePath = razorFiles[0];

                    Console.WriteLine(
                        $"Auto-discovered files:\n  " +
                        $"Class: {classFilePath}\n  Razor: {razorFilePath}");
                }
                else
                {
                    Console.WriteLine(
                        "Error: Could not automatically find exactly one .cs" +
                        " and one .razor file in the current directory.");

                    Console.WriteLine("Please specify the file paths like this:");
                    Console.WriteLine("  Litmus <ComponentClass.cs> <ComponentRazor.razor>");
                    return;
                }
            }
            else if (args.Length == 2)
            {
                classFilePath = args[0];
                razorFilePath = args[1];
            }
            else
            {
                Console.WriteLine("Usage: Litmus <ComponentClass.cs> <ComponentRazor.razor>");
                return;
            }

            if (!File.Exists(classFilePath))
            {
                Console.WriteLine($"Error: Class file not found: {classFilePath}");
                return;
            }
            if (!File.Exists(razorFilePath))
            {
                Console.WriteLine($"Error: Razor file not found: {razorFilePath}");
                return;
            }

            Console.WriteLine("Processing files...");
            var classSource = File.ReadAllText(classFilePath);
            var razorSource = File.ReadAllText(razorFilePath);
            var componentClassName = DetectClassName(classSource) ?? "UnknownComponent";
            string renderedInstanceName = "this.rendered" + componentClassName;

            var testCode =
                GenerateFullTest(
                    classSource,
                    razorSource,
                    componentClassName,
                    renderedInstanceName);

            Console.WriteLine(testCode);

            static string GenerateFullTest(
                string classSource,
                string razorSource,
                string componentClassName,
                string renderedInstanceName)
            {
                var stringBuilder = new StringBuilder();

                // Extract property null assertions only
                string defaultValuesAssertions =
                    GenerateDefaultValuesAssertions(
                        classSource,
                        $"initial{componentClassName}");

                // 2. Extract property not null assertions only
                string notNullAssertions =
                    GenerateNotNullAssertions(
                        classSource,
                        renderedInstanceName);

                // 3. Extract @ref declarations & assertions
                (string declarations, string refAssertions) =
                    ParseRazorForRefVariables(
                        razorSource,
                        renderedInstanceName);

                // Test 1: Default component values test
                stringBuilder.AppendLine("[Fact]");
                stringBuilder.AppendLine($"public void ShouldInitializeDefaultComponentValues()");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("    // given . when");
                stringBuilder.AppendLine($"    var initial{componentClassName} = new {componentClassName}();");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("    // then");
                stringBuilder.Append(defaultValuesAssertions);
                stringBuilder.AppendLine("}");

                // Test 2: Property Not Null Assertions ONLY
                stringBuilder.AppendLine("[Fact]");
                stringBuilder.AppendLine($"public void ShouldRender{componentClassName}OnInitialized()");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("    // given . when");
                stringBuilder.AppendLine($"    var rendered = RenderComponent<{componentClassName}>();");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("    // then");
                stringBuilder.Append(notNullAssertions);
                stringBuilder.AppendLine("}");

                stringBuilder.AppendLine();

                // Test 3: @ref & style assertions
                stringBuilder.AppendLine("[Fact]");
                stringBuilder.AppendLine($"public void ShouldRender{componentClassName}WithStyles()");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("    // given");
                stringBuilder.Append(declarations);
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("    // when");
                stringBuilder.AppendLine($"    this.rendered{componentClassName} = RenderComponent<{componentClassName}>();");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("    // then");
                stringBuilder.Append(refAssertions);
                stringBuilder.AppendLine("}");

                return stringBuilder.ToString();
            }


            static (string declarations, string assertions) ParseRazorForRefVariables(
                string razorText,
                string renderedInstanceName)
            {
                var tagPattern = new Regex(@"<(?<tag>\w+)([^>]*?)@ref\s*=\s*@(?<refName>\w+)([^>]*)>")
                    ;
                var attrPattern =
                    new Regex(@"(?<key>\w+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))");


                var declarations = new StringBuilder();
                var assertions = new StringBuilder();

                foreach (Match tagMatch in tagPattern.Matches(razorText))
                {
                    var refName = tagMatch.Groups["refName"].Value;
                    var fullTagText = tagMatch.Value;
                    var attributes = attrPattern.Matches(fullTagText);

                    foreach (Match attr in attributes)
                    {
                        var key = attr.Groups["key"].Value;

                        // Skip the @ref itself as attribute to avoid redundant variables
                        if (key.Equals("ref", StringComparison.OrdinalIgnoreCase)) continue;

                        var rawValue = attr.Groups["value"].Value;
                        rawValue = rawValue.TrimStart('@');

                        // Build expected variable name WITHOUT the trailing "Ref" suffix in the refName
                        // Example: refName = "IdLabelRef" → cleanedName = "IdLabel"
                        string cleanedRefName = refName;
                        if (cleanedRefName.EndsWith("Ref"))
                            cleanedRefName = cleanedRefName.Substring(0, cleanedRefName.Length - 3);

                        string expectedVarName = $"expected{cleanedRefName}{ToPascalCase(key)}";

                        string typedValue;
                        string typeHint = "var"; // fallback

                        if (rawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                            rawValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                        {
                            typedValue = rawValue.ToLower();
                            typeHint = "bool";
                        }
                        else if (rawValue.Contains('.') && char.IsUpper(rawValue[0]))
                        {
                            typedValue = rawValue;
                            typeHint = GetTypeFromEnum(rawValue);
                        }
                        else if (int.TryParse(rawValue, out _))
                        {
                            typedValue = rawValue;
                            typeHint = "int";
                        }
                        else
                        {
                            typedValue = $"\"{rawValue}\"";
                            typeHint = "string";
                        }

                        declarations.AppendLine($"    {typeHint} {expectedVarName} = {typedValue};");
                        assertions.AppendLine($"    {renderedInstanceName}.Instance.{refName}.{key}.Should().Be({expectedVarName});");
                    }
                }

                return (declarations.ToString(), assertions.ToString());
            }

            static string GetTypeFromEnum(string rawValue)
            {
                var parts = rawValue.Split('.');
                return parts.Length > 1 ? parts[0] : "/* UnknownEnumType */";
            }

            static string ToPascalCase(string input)
            {
                if (string.IsNullOrEmpty(input)) return input;

                var parts = input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                return string.Concat(parts.Select(p => char.ToUpper(p[0]) + p.Substring(1)));
            }

            static string GenerateDefaultValuesAssertions(string classSource, string instanceName)
            {
                var tree = CSharpSyntaxTree.ParseText(classSource);
                var root = tree.GetRoot();

                var classNode = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault();

                if (classNode is null)
                    return "// Could not find class declaration.";

                var sb = new StringBuilder();

                foreach (var prop in classNode.Members.OfType<PropertyDeclarationSyntax>())
                {
                    var propName = prop.Identifier.Text;

                    // Skip properties with [Parameter] attribute (optional for now)
                    if (HasParameterAttribute(prop))
                        continue;

                    if (prop.Modifiers.Any(SyntaxKind.PublicKeyword))
                    {
                        sb.AppendLine($"    {instanceName}.{propName}.Should().BeNull();");
                    }
                }

                return sb.ToString();
            }


            static string GenerateNotNullAssertions(string classSource, string renderedInstanceName)
            {
                var tree = CSharpSyntaxTree.ParseText(classSource);
                var root = tree.GetRoot();

                var classNode = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault();

                if (classNode is null)
                    return "// Could not find class declaration.";

                var sb = new StringBuilder();

                foreach (var prop in classNode.Members.OfType<PropertyDeclarationSyntax>())
                {
                    var propName = prop.Identifier.Text;

                    if (HasParameterAttribute(prop) is true)
                        continue;

                    if (prop.Modifiers.Any(SyntaxKind.PublicKeyword))
                    {
                        sb.AppendLine($"    {renderedInstanceName}.Instance.{propName}.Should().NotBeNull();");
                    }
                }

                return sb.ToString();
            }

            static bool HasParameterAttribute(PropertyDeclarationSyntax property)
            {
                // Check if property has [Parameter] attribute (optional)
                return property.AttributeLists
                    .SelectMany(a => a.Attributes)
                    .Any(attr => attr.Name.ToString().Contains("Parameter"));
            }

            static string? DetectClassName(string classSource)
            {
                var tree = CSharpSyntaxTree.ParseText(classSource);
                var root = tree.GetRoot();
                var classNode = root.DescendantNodes()
                                    .OfType<ClassDeclarationSyntax>()
                                    .FirstOrDefault();

                return classNode?.Identifier.Text;
            }
        }
    }
}
