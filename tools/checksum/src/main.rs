use sha2::{Digest, Sha256};
use std::{
    env,
    fs::File,
    io::{self, Read},
    path::Path,
    process::ExitCode,
};

const MAX_MANIFEST_BYTES: u64 = 256 * 1024;
const USAGE: &str = "Usage: vilod-checksum <downloaded-file> <SHA256SUMS.txt>";

fn expected_hash(manifest: &Path, filename: &str) -> Result<String, String> {
    let mut contents = String::new();
    File::open(manifest)
        .and_then(|file| file.take(MAX_MANIFEST_BYTES + 1).read_to_string(&mut contents))
        .map_err(|error| format!("Cannot read UTF-8 checksum manifest: {error}"))?;
    if contents.len() as u64 > MAX_MANIFEST_BYTES {
        return Err("Checksum manifest exceeds 256 KiB".into());
    }

    let mut expected = None;
    for line in contents.trim_start_matches('\u{feff}').lines() {
        if line.trim().is_empty() {
            continue;
        }
        let digest = line.get(..64).ok_or("Invalid SHA-256 manifest line")?;
        let marker = line.get(64..66).ok_or("Missing checksum filename")?;
        let name = line.get(66..).ok_or("Missing checksum filename")?;
        if !digest.bytes().all(|b| b.is_ascii_hexdigit())
            || (marker != "  " && marker != " *")
            || name.is_empty()
        {
            return Err("Invalid SHA-256 manifest line".into());
        }
        if name == filename && expected.replace(digest.to_ascii_lowercase()).is_some() {
            return Err("Checksum manifest contains duplicate entries for this file".into());
        }
    }
    expected.ok_or_else(|| "No checksum entry matches the downloaded file name".into())
}

fn sha256(path: &Path) -> io::Result<String> {
    let mut file = File::open(path)?;
    if !file.metadata()?.is_file() {
        return Err(io::Error::new(io::ErrorKind::InvalidInput, "Expected a regular file"));
    }
    let mut digest = Sha256::new();
    io::copy(&mut file, &mut digest)?;
    Ok(format!("{:x}", digest.finalize()))
}

fn verify(file: &Path, manifest: &Path) -> Result<(), String> {
    let filename = file
        .file_name()
        .and_then(|name| name.to_str())
        .ok_or("Expected a UTF-8 file name")?;
    let expected = expected_hash(manifest, filename)?;
    let actual = sha256(file).map_err(|error| format!("Cannot read download: {error}"))?;
    if actual != expected {
        return Err(format!("SHA-256 mismatch\nExpected: {expected}\nActual:   {actual}"));
    }
    println!("SHA-256 matches: {actual}");
    Ok(())
}

fn main() -> ExitCode {
    let args: Vec<_> = env::args_os().skip(1).collect();
    if args.len() == 1 && (args[0] == "--help" || args[0] == "-h") {
        println!("{USAGE}");
        return ExitCode::SUCCESS;
    }
    if args.len() != 2 {
        eprintln!("{USAGE}");
        return ExitCode::from(2);
    }
    match verify(Path::new(&args[0]), Path::new(&args[1])) {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}
