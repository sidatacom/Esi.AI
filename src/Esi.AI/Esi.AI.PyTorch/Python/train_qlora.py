import argparse
import json
from pathlib import Path

import torch
from datasets import Dataset
from peft import LoraConfig, get_peft_model, prepare_model_for_kbit_training
from transformers import (
    AutoModelForCausalLM,
    AutoTokenizer,
    BitsAndBytesConfig,
    DataCollatorForLanguageModeling,
    Trainer,
    TrainerCallback,
    TrainingArguments,
)


class ProgressCallback(TrainerCallback):
    def on_log(self, args, state, control, logs=None, **kwargs):
        logs = logs or {}
        print(
            json.dumps(
                {
                    "type": "progress",
                    "step": state.global_step,
                    "total_steps": state.max_steps,
                    "loss": logs.get("loss"),
                }
            ),
            flush=True,
        )


def load_dataset(dataset_path, tokenizer, max_sequence_length):
    examples = []
    with Path(dataset_path).open("r", encoding="utf-8") as dataset_file:
        for line_number, line in enumerate(dataset_file, start=1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError(f"Invalid JSON on line {line_number}: {error}") from error

            messages = row.get("messages")
            if not isinstance(messages, list) or not messages:
                raise ValueError(f"Line {line_number} must contain a non-empty 'messages' array.")
            token_ids = tokenizer.apply_chat_template(
                messages,
                tokenize=True,
                add_generation_prompt=False,
            )
            if len(token_ids) > max_sequence_length:
                token_ids = token_ids[:max_sequence_length]
            examples.append({"input_ids": token_ids})

    if not examples:
        raise ValueError("The JSONL dataset contains no training examples.")
    return Dataset.from_list(examples)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-id", required=True)
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--epochs", type=int, required=True)
    parser.add_argument("--max-sequence-length", type=int, required=True)
    parser.add_argument("--batch-size", type=int, required=True)
    parser.add_argument("--gradient-accumulation-steps", type=int, required=True)
    parser.add_argument("--lora-rank", type=int, required=True)
    parser.add_argument("--learning-rate", type=float, required=True)
    args = parser.parse_args()

    if not torch.xpu.is_available():
        raise RuntimeError("PyTorch cannot access an Intel XPU. Verify the driver and XPU-enabled PyTorch wheel.")

    print(f"XPU device: {torch.xpu.get_device_name(0)}", flush=True)
    tokenizer = AutoTokenizer.from_pretrained(args.model_id)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    quantization_config = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type="nf4",
        bnb_4bit_use_double_quant=True,
        bnb_4bit_compute_dtype=torch.bfloat16,
    )
    model = AutoModelForCausalLM.from_pretrained(
        args.model_id,
        quantization_config=quantization_config,
        torch_dtype=torch.bfloat16,
        device_map={"": "xpu:0"},
    )
    model.config.use_cache = False
    model = prepare_model_for_kbit_training(model)
    model = get_peft_model(
        model,
        LoraConfig(
            r=args.lora_rank,
            lora_alpha=args.lora_rank * 2,
            target_modules="all-linear",
            lora_dropout=0.05,
            bias="none",
            task_type="CAUSAL_LM",
        ),
    )
    dataset = load_dataset(args.dataset, tokenizer, args.max_sequence_length)
    training_args = TrainingArguments(
        output_dir=args.output_dir,
        num_train_epochs=args.epochs,
        per_device_train_batch_size=args.batch_size,
        gradient_accumulation_steps=args.gradient_accumulation_steps,
        learning_rate=args.learning_rate,
        logging_strategy="steps",
        logging_steps=1,
        save_strategy="no",
        report_to="none",
        bf16=True,
        gradient_checkpointing=True,
        dataloader_num_workers=0,
        optim="adamw_torch",
    )
    trainer = Trainer(
        model=model,
        args=training_args,
        train_dataset=dataset,
        data_collator=DataCollatorForLanguageModeling(tokenizer=tokenizer, mlm=False),
        processing_class=tokenizer,
        callbacks=[ProgressCallback()],
    )
    trainer.train()
    trainer.model.save_pretrained(args.output_dir)
    tokenizer.save_pretrained(args.output_dir)
    print(f"Adapter saved to {args.output_dir}", flush=True)


if __name__ == "__main__":
    main()