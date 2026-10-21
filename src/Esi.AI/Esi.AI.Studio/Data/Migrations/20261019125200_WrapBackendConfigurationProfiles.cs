using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Esi.AI.Studio.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261019125200_WrapBackendConfigurationProfiles")]
public sealed class WrapBackendConfigurationProfiles : Migration
{
    private const string VariantExpression = """
        CASE
            WHEN length(trim(COALESCE("BackendVariantId", ''))) > 0 THEN lower(trim("BackendVariantId"))
            WHEN "Backend" = 0 THEN CASE lower(COALESCE(
                json_extract("ConfigurationJson", '$.backend'),
                json_extract("ConfigurationJson", '$.Backend'), ''))
                WHEN 'vulkan' THEN 'llama.vulkan'
                WHEN 'cuda' THEN 'llama.cuda12'
                WHEN 'cuda12' THEN 'llama.cuda12'
                WHEN 'sycl' THEN 'llama.sycl'
                WHEN 'sycl16' THEN 'llama.sycl'
                WHEN 'xpu' THEN 'llama.sycl'
                ELSE 'llama.vulkan'
            END
            WHEN "Backend" = 1 THEN 'openvino'
            WHEN "Backend" = 2 THEN CASE
                WHEN lower(COALESCE(
                    json_extract("ConfigurationJson", '$.device'),
                    json_extract("ConfigurationJson", '$.Device'), '')) LIKE 'xpu:%'
                    OR EXISTS (
                        SELECT 1 FROM json_each("ConfigurationJson", '$.devices') AS configured_device
                        WHERE lower(configured_device.value) LIKE 'xpu:%')
                    OR EXISTS (
                        SELECT 1 FROM json_each("ConfigurationJson", '$.Devices') AS configured_device
                        WHERE lower(configured_device.value) LIKE 'xpu:%')
                    OR COALESCE(
                        json_extract("ConfigurationJson", '$.enableXpuGraph'),
                        json_extract("ConfigurationJson", '$.EnableXpuGraph'), 0) = 1
                THEN 'vllm.xpu'
                ELSE 'vllm.cuda12'
            END
            WHEN "Backend" = 3 THEN 'sglang.cuda12'
            WHEN "Backend" = 4 THEN 'dotllm.cpu'
            ELSE ''
        END
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(WrapTable("ModelConfigurations"));
        migrationBuilder.Sql(WrapTable("ModelSettings"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(UnwrapTable("ModelConfigurations"));
        migrationBuilder.Sql(UnwrapTable("ModelSettings"));
    }

    private static string WrapTable(string tableName) => $"""
        UPDATE "{tableName}"
        SET "ConfigurationJson" = json_object(({VariantExpression}), json("ConfigurationJson"))
        WHERE json_valid("ConfigurationJson")
          AND json_type("ConfigurationJson", '$') = 'object'
          AND ({VariantExpression}) <> ''
          AND NOT EXISTS (
              SELECT 1 FROM json_each("ConfigurationJson") AS existing_block
              WHERE existing_block.key = 'openvino'
                 OR existing_block.key LIKE 'llama.%'
                 OR existing_block.key LIKE 'vllm.%'
                 OR existing_block.key LIKE 'sglang.%'
                 OR existing_block.key LIKE 'dotllm.%')
        """;

    private static string UnwrapTable(string tableName) => $"""
        UPDATE "{tableName}"
        SET "ConfigurationJson" = json_extract(
            "ConfigurationJson",
            '$."' || (SELECT key FROM json_each("ConfigurationJson") LIMIT 1) || '"')
        WHERE json_valid("ConfigurationJson")
          AND json_type("ConfigurationJson", '$') = 'object'
          AND (SELECT count(*) FROM json_each("ConfigurationJson")) = 1
          AND EXISTS (
              SELECT 1 FROM json_each("ConfigurationJson") AS existing_block
              WHERE existing_block.key = 'openvino'
                 OR existing_block.key LIKE 'llama.%'
                 OR existing_block.key LIKE 'vllm.%'
                 OR existing_block.key LIKE 'sglang.%'
                 OR existing_block.key LIKE 'dotllm.%')
        """;
}