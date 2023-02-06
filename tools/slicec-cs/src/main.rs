// Copyright (c) ZeroC, Inc.

mod attribute_patcher;
mod builders;
mod comment_patcher;
mod comments;
mod cs_attributes;
mod cs_options;
mod cs_util;
mod decoding;
mod encoded_result;
mod encoding;
mod generated_code;
mod member_util;
mod slicec_ext;
mod validators;
mod visitors;

use attribute_patcher::patch_attributes;
use comment_patcher::patch_comments;
use cs_options::CsOptions;
use generated_code::GeneratedCode;
use slice::clap::Parser;
use slice::compilation_result::CompilationResult;
use slice::diagnostics::{Error, ErrorKind};
use slice::slice_file::SliceFile;
use std::fs::File;
use std::io;
use std::io::prelude::*;
use std::path::Path;
use validators::cs_validator::validate_cs_attributes;
use visitors::{
    ClassVisitor, DispatchVisitor, EnumVisitor, ExceptionVisitor, ModuleVisitor, ProxyVisitor, StructVisitor,
};

use slice::code_block::CodeBlock;

pub fn main() {
    let compilation_data = match try_main() {
        Ok(data) => data,
        Err(data) => data,
    };
    std::process::exit(compilation_data.into_exit_code());
}

fn try_main() -> CompilationResult {
    let options = CsOptions::parse();
    let slice_options = &options.slice_options;
    let mut compilation_data = slice::compile_from_options(slice_options)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)?;

    if !slice_options.dry_run {
        for slice_file in compilation_data.files.values().filter(|file| file.is_source) {
            let mut generated_code = GeneratedCode::new();

            generated_code.preamble.push(preamble(slice_file));

            let mut struct_visitor = StructVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut struct_visitor);

            let mut proxy_visitor = ProxyVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut proxy_visitor);

            let mut dispatch_visitor = DispatchVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut dispatch_visitor);

            let mut exception_visitor = ExceptionVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut exception_visitor);

            let mut enum_visitor = EnumVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut enum_visitor);

            let mut class_visitor = ClassVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut class_visitor);

            let mut module_visitor = ModuleVisitor {
                generated_code: &mut generated_code,
            };
            slice_file.visit_with(&mut module_visitor);

            {
                let path = match &slice_options.output_dir {
                    Some(output_dir) => Path::new(output_dir),
                    _ => Path::new("."),
                }
                .join(format!("{}.cs", &slice_file.filename))
                .to_owned();

                // Move the generated code out of the generated_code struct and consolidate into a
                // single string.
                let code_string = generated_code
                    .preamble
                    .into_iter()
                    .chain(generated_code.code_blocks.into_iter())
                    .collect::<CodeBlock>()
                    .to_string();

                // If the file already exists and its contents match the generated code, we don't re-write it.
                if matches!(std::fs::read(&path), Ok(file_bytes) if file_bytes == code_string.as_bytes()) {
                    continue;
                }

                match write_file(&path, &code_string) {
                    Ok(_) => (),
                    Err(error) => {
                        Error::new(ErrorKind::IO {
                            action: "write",
                            path: path.display().to_string(),
                            error,
                        })
                        .report(&mut compilation_data.diagnostic_reporter);
                        continue;
                    }
                }
            }
        }
    }

    compilation_data.into()
}

fn preamble(slice_file: &SliceFile) -> CodeBlock {
    format!(
        r#"// Copyright (c) ZeroC, Inc.

// <auto-generated/>
// slicec-cs version: '{version}'
// Generated from file: '{file}.slice'

#nullable enable

#pragma warning disable 1591 // Missing XML Comment
#pragma warning disable 1573 // Parameter has no matching param tag in the XML comment

using IceRpc.Slice;

[assembly:IceRpc.Slice.Slice("{file}.slice")]"#,
        version = env!("CARGO_PKG_VERSION"),
        file = slice_file.filename,
    )
    .into()
}

fn write_file(path: &Path, contents: &str) -> Result<(), io::Error> {
    let mut file = File::create(path)?;
    file.write_all(contents.as_bytes())
}
