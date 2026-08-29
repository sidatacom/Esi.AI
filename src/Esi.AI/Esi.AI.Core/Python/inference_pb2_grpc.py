"""gRPC bindings for the local Esi.AI inference bridge."""

import grpc

from inference_pb2 import (
    GenerateRequest,
    GenerateResponse,
    LoadModelRequest,
    ModelOperationResponse,
    ReadinessRequest,
    ReadinessResponse,
    UnloadModelRequest,
)


class InferenceStub:
    def __init__(self, channel):
        self.CheckReadiness = channel.unary_unary(
            "/esi.ai.inference.Inference/CheckReadiness",
            request_serializer=ReadinessRequest.SerializeToString,
            response_deserializer=ReadinessResponse.FromString,
        )
        self.LoadModel = channel.unary_unary(
            "/esi.ai.inference.Inference/LoadModel",
            request_serializer=LoadModelRequest.SerializeToString,
            response_deserializer=ModelOperationResponse.FromString,
        )
        self.UnloadModel = channel.unary_unary(
            "/esi.ai.inference.Inference/UnloadModel",
            request_serializer=UnloadModelRequest.SerializeToString,
            response_deserializer=ModelOperationResponse.FromString,
        )
        self.Generate = channel.unary_stream(
            "/esi.ai.inference.Inference/Generate",
            request_serializer=GenerateRequest.SerializeToString,
            response_deserializer=GenerateResponse.FromString,
        )


class InferenceServicer:
    async def CheckReadiness(self, request, context):
        await context.abort(grpc.StatusCode.UNIMPLEMENTED, "Method not implemented")

    async def LoadModel(self, request, context):
        await context.abort(grpc.StatusCode.UNIMPLEMENTED, "Method not implemented")

    async def UnloadModel(self, request, context):
        await context.abort(grpc.StatusCode.UNIMPLEMENTED, "Method not implemented")

    async def Generate(self, request, context):
        await context.abort(grpc.StatusCode.UNIMPLEMENTED, "Method not implemented")


def add_InferenceServicer_to_server(servicer, server):
    rpc_method_handlers = {
        "CheckReadiness": grpc.unary_unary_rpc_method_handler(
            servicer.CheckReadiness,
            request_deserializer=ReadinessRequest.FromString,
            response_serializer=ReadinessResponse.SerializeToString,
        ),
        "LoadModel": grpc.unary_unary_rpc_method_handler(
            servicer.LoadModel,
            request_deserializer=LoadModelRequest.FromString,
            response_serializer=ModelOperationResponse.SerializeToString,
        ),
        "UnloadModel": grpc.unary_unary_rpc_method_handler(
            servicer.UnloadModel,
            request_deserializer=UnloadModelRequest.FromString,
            response_serializer=ModelOperationResponse.SerializeToString,
        ),
        "Generate": grpc.unary_stream_rpc_method_handler(
            servicer.Generate,
            request_deserializer=GenerateRequest.FromString,
            response_serializer=GenerateResponse.SerializeToString,
        ),
    }
    generic_handler = grpc.method_handlers_generic_handler(
        "esi.ai.inference.Inference", rpc_method_handlers
    )
    server.add_generic_rpc_handlers((generic_handler,))