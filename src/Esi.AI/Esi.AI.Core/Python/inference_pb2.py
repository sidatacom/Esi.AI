"""Runtime protobuf types for the local Esi.AI inference bridge.

The C# client is generated from the same vllm_inference.proto file. Keeping the
descriptor here avoids requiring protoc or grpcio-tools in the user's runtime
environment; grpcio and protobuf remain the only Python runtime dependencies.
"""

from google.protobuf import descriptor_pb2, descriptor_pool, message_factory


def _field(message, name, number, field_type, label=1, type_name=None):
    field_descriptor = message.field.add()
    field_descriptor.name = name
    field_descriptor.number = number
    field_descriptor.type = field_type
    field_descriptor.label = label
    if type_name:
        field_descriptor.type_name = type_name


file_descriptor = descriptor_pb2.FileDescriptorProto(
    name="vllm_inference.proto",
    package="esi.ai.inference",
    syntax="proto3",
)

for name in ("ReadinessRequest", "UnloadModelRequest"):
    file_descriptor.message_type.add(name=name)

readiness_response = file_descriptor.message_type.add(name="ReadinessResponse")
_field(readiness_response, "ready", 1, 8)
_field(readiness_response, "model_loaded", 2, 8)
_field(readiness_response, "model_id", 3, 9)
_field(readiness_response, "error", 4, 9)

load_request = file_descriptor.message_type.add(name="LoadModelRequest")
_field(load_request, "model_path", 1, 9)
_field(load_request, "engine", 2, 9)
_field(load_request, "max_model_len", 3, 13)
_field(load_request, "tensor_parallel_size", 4, 13)
_field(load_request, "gpu_memory_utilization", 5, 2)
_field(load_request, "trust_remote_code", 6, 8)
_field(load_request, "enforce_eager", 7, 8)
_field(load_request, "device", 8, 9)
_field(load_request, "devices", 9, 9, 3)

operation_response = file_descriptor.message_type.add(name="ModelOperationResponse")
_field(operation_response, "succeeded", 1, 8)
_field(operation_response, "model_id", 2, 9)
_field(operation_response, "error", 3, 9)

chat_message = file_descriptor.message_type.add(name="ChatMessage")
_field(chat_message, "role", 1, 9)
_field(chat_message, "content", 2, 9)

generate_request = file_descriptor.message_type.add(name="GenerateRequest")
_field(generate_request, "request_id", 1, 9)
_field(generate_request, "model_id", 2, 9)
_field(generate_request, "messages", 3, 11, 3, ".esi.ai.inference.ChatMessage")
_field(generate_request, "max_tokens", 4, 13)
_field(generate_request, "temperature", 5, 2)
_field(generate_request, "top_p", 6, 2)

generate_response = file_descriptor.message_type.add(name="GenerateResponse")
_field(generate_response, "delta", 1, 9)
_field(generate_response, "finished", 2, 8)
_field(generate_response, "generated_tokens", 3, 13)
_field(generate_response, "prompt_tokens", 4, 13)
_field(generate_response, "tokens_per_second", 5, 1)
_field(generate_response, "error", 6, 9)

service = file_descriptor.service.add(name="Inference")
for name, request_type, response_type, server_streaming in (
    ("CheckReadiness", "ReadinessRequest", "ReadinessResponse", False),
    ("LoadModel", "LoadModelRequest", "ModelOperationResponse", False),
    ("UnloadModel", "UnloadModelRequest", "ModelOperationResponse", False),
    ("Generate", "GenerateRequest", "GenerateResponse", True),
):
    method = service.method.add(name=name)
    method.input_type = ".esi.ai.inference." + request_type
    method.output_type = ".esi.ai.inference." + response_type
    method.server_streaming = server_streaming

DESCRIPTOR = descriptor_pool.Default().Add(file_descriptor)

ReadinessRequest = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["ReadinessRequest"])
ReadinessResponse = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["ReadinessResponse"])
LoadModelRequest = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["LoadModelRequest"])
UnloadModelRequest = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["UnloadModelRequest"])
ModelOperationResponse = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["ModelOperationResponse"])
ChatMessage = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["ChatMessage"])
GenerateRequest = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["GenerateRequest"])
GenerateResponse = message_factory.GetMessageClass(DESCRIPTOR.message_types_by_name["GenerateResponse"])