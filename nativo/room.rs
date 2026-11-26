use std::{collections::{HashMap,VecDeque}, env, io::{self,Read,Write,BufRead,BufReader}, net::{TcpListener,TcpStream}, sync::{Arc,Condvar,Mutex,atomic::{AtomicUsize,Ordering}}, thread,time::{Duration,Instant}};
#[link(name="bcrypt")]
extern "system"{fn BCryptGenRandom(alg:isize,buf:*mut u8,size:u32,flags:u32)->i32;}
fn token()->String{let mut b=[0u8;24];assert_eq!(unsafe{BCryptGenRandom(0,b.as_mut_ptr(),24,2)},0);b.iter().map(|v|format!("{v:02x}")).collect()}
struct Seat{id:String,name:String,approved:bool,seen:Instant}
struct Room{owner:String,invite:String,seats:HashMap<String,Seat>,speaker:String,epoch:String,seq:u64,packets:VecDeque<(u64,Vec<u8>)>,last:Instant}
type Shared=(Mutex<Room>,Condvar);
struct Reply(u16,Vec<u8>);
fn msg(code:u16,text:&str)->Reply{Reply(code,text.as_bytes().to_vec())}
fn clear(r:&mut Room){r.speaker.clear();r.epoch.clear();r.packets.clear();}
fn action(shared:&Shared,method:&str,path:&str,key:&str,data:Vec<u8>)->Reply{
 let mut r=shared.0.lock().unwrap();
 r.seats.retain(|_,s|s.seen.elapsed()<Duration::from_secs(45));
 if !r.speaker.is_empty()&&r.last.elapsed()>Duration::from_secs(20){clear(&mut r);shared.1.notify_all();}
 if path=="/join"&&method=="POST"&&key==r.invite{
  let name=String::from_utf8_lossy(&data).trim().to_string();
  if name.is_empty()||name.len()>80||name.chars().any(char::is_control)||r.seats.len()>=16{return msg(400,"Nome inválido ou sala cheia.")}
  let auth=token();r.seats.insert(auth.clone(),Seat{id:token(),name,approved:false,seen:Instant::now()});return msg(200,&auth)
 }
 let host=key==r.owner;
 let approved=if host{true}else if let Some(s)=r.seats.get_mut(key){s.seen=Instant::now();s.approved}else{return msg(403,"Convite ou acesso encerrado.")};
 if method=="GET"&&path=="/pending"&&host{
  return msg(200,&r.seats.values().filter(|s|!s.approved).map(|s|format!("{} {}",s.id,s.name)).collect::<Vec<_>>().join("\n"))
 }
 if method=="POST"&&host&&(path.starts_with("/accept/")||path.starts_with("/deny/")){
  let id=path.rsplit('/').next().unwrap_or("");
  if path.starts_with("/deny/"){r.seats.retain(|_,s|s.id!=id)}else if let Some(s)=r.seats.values_mut().find(|s|s.id==id){s.approved=true}
  return msg(200,"OK")
 }
 if path=="/state"&&method=="GET"{return msg(200,if !approved{"pending"}else if r.speaker.is_empty(){"idle"}else{&r.epoch})}
 if !approved{return msg(403,"Aguardando aprovação.")}
 if method=="POST"&&path=="/claim"{
  if !r.speaker.is_empty(){return msg(409,"Já há uma apresentação.")}
  r.speaker=key.to_owned();r.epoch=token();r.seq=0;r.last=Instant::now();return msg(200,&r.epoch)
 }
 let parts:Vec<_>=path.trim_start_matches('/').split('/').collect();
 if parts.len()<2||parts[1]!=r.epoch||r.epoch.is_empty(){return msg(409,"A apresentação mudou.")}
 if method=="POST"&&r.speaker==key{
  if parts[0]=="stop"{clear(&mut r);shared.1.notify_all();return msg(200,"OK")}
  if parts[0]=="send" && !data.is_empty(){r.last=Instant::now();r.seq+=1;let seq=r.seq;r.packets.push_back((seq,data));while r.packets.len()>24{r.packets.pop_front();}shared.1.notify_all();return msg(200,"OK")}
 }
 if method=="GET"&&parts.len()==3&&parts[0]=="read"{
  let Ok(cursor)=parts[2].parse::<u64>()else{return msg(400,"Cursor inválido.")};
  if r.seq<=cursor{r=shared.1.wait_timeout(r,Duration::from_millis(900)).unwrap().0;}
  if r.epoch!=parts[1]{return msg(409,"A apresentação terminou.")}
  let mut bytes:Vec<u8>=Vec::new();let mut last=cursor;
  for (id,packet) in &r.packets{if *id>cursor{bytes.extend_from_slice(packet);last=*id;}}
  let mut out=last.to_le_bytes().to_vec();out.extend(bytes);return Reply(200,out)
 }
 msg(403,"Operação indisponível.")
}
fn serve(mut conn:TcpStream,room:&Shared)->io::Result<()>{
 conn.set_read_timeout(Some(Duration::from_secs(5)))?;conn.set_write_timeout(Some(Duration::from_secs(5)))?;
 let mut input=BufReader::new((&conn).take(8192));let mut line=String::new();input.read_line(&mut line)?;
 let request=line.clone();let mut fields=request.split_whitespace();let method=fields.next().unwrap_or("");let path=fields.next().unwrap_or("");
 let mut key=String::new();let mut len=0usize;
 loop{line.clear();if input.read_line(&mut line)?==0{return Ok(())}if line=="\r\n"{break}
  if let Some((field,value))=line.split_once(':'){if field.eq_ignore_ascii_case("authorization"){key=value.trim().strip_prefix("Bearer ").unwrap_or("").to_owned()}
   if field.eq_ignore_ascii_case("content-length"){len=value.trim().parse().unwrap_or(usize::MAX)}}
 }
 if len>512*1024{return Ok(())}
 let buffered=input.buffer().to_vec();drop(input);let mut data=vec![0;len];let n=len.min(buffered.len());data[..n].copy_from_slice(&buffered[..n]);conn.read_exact(&mut data[n..])?;
 let Reply(code,body)=action(room,method,path,&key,data);
 write!(conn,"HTTP/1.1 {code} Result\r\nContent-Type: application/octet-stream\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {}\r\n\r\n",body.len())?;conn.write_all(&body)
}
fn main()->io::Result<()>{
 let owner=env::var("LIVEZ_OWNER").map_err(io::Error::other)?;let invite=env::var("LIVEZ_INVITE").map_err(io::Error::other)?;
 let listener=TcpListener::bind("127.0.0.1:0")?;println!("{}",listener.local_addr()?.port());io::stdout().flush()?;
 let room=Arc::new((Mutex::new(Room{owner,invite,seats:HashMap::new(),speaker:String::new(),epoch:String::new(),seq:0,packets:VecDeque::new(),last:Instant::now()}),Condvar::new()));
 thread::spawn(||{let mut b=[0];let _=io::stdin().read(&mut b);std::process::exit(0)});
 let count=Arc::new(AtomicUsize::new(0));
 for sock in listener.incoming().flatten(){if count.fetch_add(1,Ordering::Relaxed)>=48{count.fetch_sub(1,Ordering::Relaxed);continue}let c=count.clone();let r=room.clone();thread::spawn(move||{let _=serve(sock,&r);c.fetch_sub(1,Ordering::Relaxed);});}
 Ok(())
}
